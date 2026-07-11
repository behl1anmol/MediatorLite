using FluentAssertions;
using MediatorLite.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace MediatorLite.Tests.UnitTests;

/// <summary>
/// Drives <see cref="HandlerDiscoveryGenerator"/> in-memory to pin behavior that cannot be
/// observed from inside a normally-compiled test assembly: generator diagnostics that would
/// fail this project's build (warnings are errors), and incremental-caching guarantees.
/// </summary>
public class SourceGeneratorDriverTests
{
    private const string HandlerSource = """
        using MediatorLite;

        namespace DriverTests;

        public record PingQuery(int Value) : IRequest<int>;

        public sealed class PingQueryHandler : IRequestHandler<PingQuery, int>
        {
            public ValueTask<int> HandleAsync(PingQuery request, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(request.Value);
        }
        """;

    [Fact]
    public void ConstrainedOpenBehavior_ReportsMedl1002_AndIsNotRegistered()
    {
        // An open behavior with a constraint beyond `where TRequest : IRequest<TResponse>`
        // cannot be expanded over every request type; emitting it anyway used to produce
        // generated code that failed to compile with opaque errors in the .g.cs file.
        const string behaviorSource = """
            using MediatorLite;

            namespace DriverTests;

            public interface IAuditable;

            public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>, IAuditable
            {
                public ValueTask<TResponse> HandleAsync(
                    TRequest request,
                    RequestHandlerDelegate<TResponse> next,
                    CancellationToken cancellationToken = default)
                    => next();
            }
            """;

        var (runResult, _) = RunGenerator(HandlerSource, behaviorSource);

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "MEDL1002")
            .Which.GetMessage().Should().Contain("AuditBehavior");

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().NotContain("AuditBehavior",
            "an unexpandable open behavior must be skipped, not emitted as non-compiling code");
    }

    [Fact]
    public void GenericBehaviorClass_WithFullyClosedInterface_ReportsMedl1002_AndIsNotEmitted()
    {
        // The class is generic but the IPipelineBehavior<,> interface is fully closed, so the
        // class's type parameter cannot be bound from the interface. Emitting the class's
        // display name would paste `UnusedParamBehavior<TUnused>` verbatim into the generated
        // registration/pipeline, where TUnused is an unknown identifier (CS0246).
        const string behaviorSource = """
            using MediatorLite;

            namespace DriverTests;

            public sealed class UnusedParamBehavior<TUnused> : IPipelineBehavior<PingQuery, int>
            {
                public ValueTask<int> HandleAsync(
                    PingQuery request,
                    RequestHandlerDelegate<int> next,
                    CancellationToken cancellationToken = default)
                    => next();
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, behaviorSource);

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "MEDL1002")
            .Which.GetMessage().Should().Contain("UnusedParamBehavior");

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().NotContain("UnusedParamBehavior",
            "a generic behavior whose interface cannot bind its type parameters must be skipped");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void GenericBehaviorClass_WithPartiallyClosedInterface_ReportsMedl1002_AndIsNotEmitted()
    {
        // The type parameter is nested inside the interface's request type argument
        // (IPipelineBehavior<Wrap<T>, int>). The top-level type args are named types, so the
        // old top-level-only openness check treated this as a closed behavior and emitted
        // `Wrap<T>` / `PartialBehavior<T>` with an unbound T.
        const string behaviorSource = """
            using MediatorLite;

            namespace DriverTests;

            public record Wrap<T>(T Value) : IRequest<int>;

            public sealed class WrapHandler : IRequestHandler<Wrap<string>, int>
            {
                public ValueTask<int> HandleAsync(Wrap<string> request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(0);
            }

            public sealed class PartialBehavior<T> : IPipelineBehavior<Wrap<T>, int>
            {
                public ValueTask<int> HandleAsync(
                    Wrap<T> request,
                    RequestHandlerDelegate<int> next,
                    CancellationToken cancellationToken = default)
                    => next();
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, behaviorSource);

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "MEDL1002")
            .Which.GetMessage().Should().Contain("PartialBehavior");

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().NotContain("PartialBehavior",
            "a behavior whose interface args contain unbindable type parameters must be skipped");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void CanonicalOpenBehavior_DoesNotReportMedl1002()
    {
        const string behaviorSource = """
            using MediatorLite;

            namespace DriverTests;

            public sealed class PassThroughBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
            {
                public ValueTask<TResponse> HandleAsync(
                    TRequest request,
                    RequestHandlerDelegate<TResponse> next,
                    CancellationToken cancellationToken = default)
                    => next();
            }
            """;

        var (runResult, _) = RunGenerator(HandlerSource, behaviorSource);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "MEDL1002");
        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().Contain("PassThroughBehavior",
            "the canonical open shape must still expand over every request type");
    }

    [Fact]
    public void NotificationStrategyAttributes_OnTypeFromReferencedAssembly_AreHonored()
    {
        // Standard consumer layout: notification contracts live in a referenced assembly,
        // handlers live in the compilation the generator runs on. The strategy attributes
        // travel with the notification type and must be honored even though the type's
        // declaration syntax is not part of this compilation.
        const string contractsSource = """
            using MediatorLite;

            namespace Contracts;

            [NotificationExecution(NotificationExecutionStrategy.Parallel)]
            [NotificationError(NotificationErrorStrategy.ContinueAndAggregate)]
            public record CrossAssemblyEvent(string Message) : INotification;
            """;

        const string handlersSource = """
            using Contracts;
            using MediatorLite;

            namespace DriverTests;

            public sealed class CrossAssemblyEventHandler1 : INotificationHandler<CrossAssemblyEvent>
            {
                public ValueTask HandleAsync(CrossAssemblyEvent notification, CancellationToken cancellationToken = default)
                    => ValueTask.CompletedTask;
            }

            public sealed class CrossAssemblyEventHandler2 : INotificationHandler<CrossAssemblyEvent>
            {
                public ValueTask HandleAsync(CrossAssemblyEvent notification, CancellationToken cancellationToken = default)
                    => ValueTask.CompletedTask;
            }
            """;

        var contractsReference = EmitToMetadataReference("Contracts", contractsSource);
        var compilation = CreateCompilation([handlersSource], [contractsReference]);

        var driver = CSharpGeneratorDriver.Create(new HandlerDiscoveryGenerator());
        var ranDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        var runResult = ranDriver.GetRunResult();

        var publisher = runResult.GeneratedTrees
            .Select(t => t.ToString())
            .Single(t => t.Contains("Publish_"));

        // Parallel + ContinueAndAggregate emits the two-phase start/await pattern with one
        // ValueTask local per handler; Sequential (the silent-fallback bug) awaits handlers
        // one at a time and declares no vt locals.
        publisher.Should().Contain("vt1").And.Contain("vt2",
            "the [NotificationExecution(Parallel)] declared on the referenced assembly's type must be honored");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void InterfaceTypedRequest_WithConcreteImplementorHandler_EmitsConcreteArmFirst_AndCompiles()
    {
        // A handler may target an interface request type (IRequestHandler<ITagged, int>)
        // alongside a handler for a concrete implementor (IRequestHandler<TaggedQuery, int>).
        // The switch must emit the concrete arm before the interface arm: the interface
        // handler is declared first here, so index-order emission would put `case ITagged`
        // first and the later `case TaggedQuery` arm would be subsumed (CS8120).
        const string source = """
            using MediatorLite;

            namespace DriverTests;

            public interface ITagged : IRequest<int>;

            public sealed class TaggedInterfaceHandler : IRequestHandler<ITagged, int>
            {
                public ValueTask<int> HandleAsync(ITagged request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(1);
            }

            public record TaggedQuery(int Value) : ITagged;

            public sealed class TaggedQueryHandler : IRequestHandler<TaggedQuery, int>
            {
                public ValueTask<int> HandleAsync(TaggedQuery request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(2);
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, source);

        var mediator = runResult.GeneratedTrees
            .Select(t => t.ToString())
            .Single(t => t.Contains("class SourceGeneratedMediator"));

        var concreteArm = mediator.IndexOf("case global::DriverTests.TaggedQuery", StringComparison.Ordinal);
        var interfaceArm = mediator.IndexOf("case global::DriverTests.ITagged", StringComparison.Ordinal);
        concreteArm.Should().BeGreaterThan(-1);
        interfaceArm.Should().BeGreaterThan(-1);
        concreteArm.Should().BeLessThan(interfaceArm,
            "the concrete implementor's arm must precede the interface arm or it is subsumed");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void RequestTypes_WhoseSanitizedNamesCollide_StillGenerateCompilableDispatch()
    {
        // GetSafeTypeName maps every non-identifier character to '_', so distinct types can
        // collapse to the same identifier: App.Get_User and App.Get.User both sanitize to
        // "App_Get_User". Without collision handling the mediator gets two identical
        // Send_App_Get_User methods (CS0111).
        const string source = """
            using MediatorLite;

            namespace App
            {
                public record Get_User(int Id) : IRequest<int>;

                public sealed class Get_UserHandler : IRequestHandler<Get_User, int>
                {
                    public ValueTask<int> HandleAsync(Get_User request, CancellationToken cancellationToken = default)
                        => ValueTask.FromResult(1);
                }
            }

            namespace App.Get
            {
                public record User(int Id) : IRequest<int>;

                public sealed class UserHandler : IRequestHandler<User, int>
                {
                    public ValueTask<int> HandleAsync(User request, CancellationToken cancellationToken = default)
                        => ValueTask.FromResult(2);
                }
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(source);

        var mediator = runResult.GeneratedTrees
            .Select(t => t.ToString())
            .Single(t => t.Contains("class SourceGeneratedMediator"));

        mediator.Should().Contain("case global::App.Get_User")
            .And.Contain("case global::App.Get.User", "both request types must keep their own dispatch arm");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void MultiResponseRequest_WithCollidingSanitizedResponseNames_StillGeneratesCompilableDispatch()
    {
        // Distinct request types that sanitize to the same name merely overload Send_* (legal).
        // The breaking case is a single request type handled for two response types whose
        // sanitized names collide: the two Send_* methods then share the parameter list and
        // differ only by return type — CS0111 without collision handling.
        const string source = """
            using MediatorLite;

            namespace App
            {
                public record Res_X(int Value);
            }

            namespace App.Res
            {
                public record X(int Value);
            }

            namespace App.Requests
            {
                public record MultiQuery(int Id) : IRequest<App.Res_X>, IRequest<App.Res.X>;

                public sealed class MultiQueryResXHandler : IRequestHandler<MultiQuery, App.Res_X>
                {
                    public ValueTask<App.Res_X> HandleAsync(MultiQuery request, CancellationToken cancellationToken = default)
                        => ValueTask.FromResult(new App.Res_X(1));
                }

                public sealed class MultiQueryXHandler : IRequestHandler<MultiQuery, App.Res.X>
                {
                    public ValueTask<App.Res.X> HandleAsync(MultiQuery request, CancellationToken cancellationToken = default)
                        => ValueTask.FromResult(new App.Res.X(2));
                }
            }
            """;

        var (_, updatedCompilation) = RunGeneratorAndUpdateCompilation(source);

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void ForeignSameNamedAttributes_DoNotChangeGeneratorBehavior()
    {
        // Attribute discovery must match namespace + name. A same-named attribute from a
        // foreign namespace (here: DisableMediatorLogging and NotificationExecution) must not
        // flip generator behavior.
        const string source = """
            using MediatorLite;

            [assembly: Foreign.DisableMediatorLogging]

            namespace Foreign
            {
                [AttributeUsage(AttributeTargets.Assembly)]
                public sealed class DisableMediatorLoggingAttribute : Attribute { }

                [AttributeUsage(AttributeTargets.Class)]
                public sealed class NotificationExecutionAttribute(int strategy) : Attribute
                {
                    public int Strategy { get; } = strategy;
                }
            }

            namespace DriverTests
            {
                [Foreign.NotificationExecution(1)] // would be Parallel if honored
                public record ForeignAttributedEvent(string Message) : INotification;

                public sealed class ForeignAttributedEventHandler1 : INotificationHandler<ForeignAttributedEvent>
                {
                    public ValueTask HandleAsync(ForeignAttributedEvent notification, CancellationToken cancellationToken = default)
                        => ValueTask.CompletedTask;
                }

                public sealed class ForeignAttributedEventHandler2 : INotificationHandler<ForeignAttributedEvent>
                {
                    public ValueTask HandleAsync(ForeignAttributedEvent notification, CancellationToken cancellationToken = default)
                        => ValueTask.CompletedTask;
                }
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, source);

        var mediator = runResult.GeneratedTrees
            .Select(t => t.ToString())
            .Single(t => t.Contains("class SourceGeneratedMediator"));

        mediator.Should().Contain("LogDebug",
            "a foreign DisableMediatorLoggingAttribute must not disable generated logging");
        mediator.Should().NotContain("vt1",
            "a foreign NotificationExecutionAttribute must not switch the event to Parallel");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void DuplicateHandlersForSameRequestAndResponse_ReportMedl1003()
    {
        const string duplicateSource = """
            using MediatorLite;

            namespace DriverTests;

            public sealed class PingQueryHandler2 : IRequestHandler<PingQuery, int>
            {
                public ValueTask<int> HandleAsync(PingQuery request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(request.Value + 1);
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, duplicateSource);

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "MEDL1003")
            .Which.GetMessage().Should().Contain("PingQuery").And.Contain("PingQueryHandler2");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void SingleHandlerPerRequest_DoesNotReportMedl1003()
    {
        var (runResult, _) = RunGenerator(HandlerSource);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "MEDL1003");
    }

    [Fact]
    public void GenericRequestType_TagLiterals_ContainNoGlobalPrefix()
    {
        // Display strings in emitted tag/log literals must strip every global:: prefix,
        // including the ones nested inside generic type arguments.
        const string source = """
            using MediatorLite;

            namespace DriverTests;

            public record Payload(int Value);

            public record GenericQuery<T>(T Value) : IRequest<int>;

            public sealed class GenericQueryPayloadHandler : IRequestHandler<GenericQuery<Payload>, int>
            {
                public ValueTask<int> HandleAsync(GenericQuery<Payload> request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(0);
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(source);

        var mediator = runResult.GeneratedTrees
            .Select(t => t.ToString())
            .Single(t => t.Contains("class SourceGeneratedMediator"));

        mediator.Should().Contain("\"DriverTests.GenericQuery<DriverTests.Payload>\"",
            "the RequestType tag literal must be fully display-formatted");
        mediator.Should().NotContain("\"DriverTests.GenericQuery<global::",
            "tag string literals must not leak an inner global:: prefix");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void UnrelatedEdit_LeavesGeneratorOutputsCached()
    {
        // The pipeline models must have value equality end-to-end: an edit that does not
        // change any discovered handler/behavior/validator must leave the output node
        // cached instead of re-emitting both source files on every keystroke.
        var compilation = CreateCompilation(HandlerSource);

        var driver = CSharpGeneratorDriver.Create(
            [new HandlerDiscoveryGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        var afterFirstRun = driver.RunGenerators(compilation);

        // Simulate an unrelated edit: a new tree that declares no handler candidates.
        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace DriverTests { public class Unrelated { } }"));

        var secondRunResult = afterFirstRun.RunGenerators(edited)
            .GetRunResult().Results.Single();

        var outputReasons = secondRunResult.TrackedOutputSteps
            .SelectMany(kvp => kvp.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToList();

        outputReasons.Should().NotBeEmpty();
        outputReasons.Should().OnlyContain(
            reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged,
            "an edit that changes no discovered model must not regenerate sources");
    }

    private static (GeneratorDriverRunResult RunResult, Compilation Compilation) RunGenerator(params string[] sources)
    {
        var compilation = CreateCompilation(sources);

        var driver = CSharpGeneratorDriver.Create(new HandlerDiscoveryGenerator());
        var runResult = driver.RunGenerators(compilation).GetRunResult();

        return (runResult, compilation);
    }

    private static (GeneratorDriverRunResult RunResult, Compilation UpdatedCompilation) RunGeneratorAndUpdateCompilation(
        params string[] sources)
    {
        var compilation = CreateCompilation(sources);

        var driver = CSharpGeneratorDriver.Create(new HandlerDiscoveryGenerator());
        var ranDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);

        return (ranDriver.GetRunResult(), updatedCompilation);
    }

    /// <summary>
    /// Asserts the compilation (consumer sources + generated trees) has no errors — the
    /// generator must never emit code that fails to compile, whatever the consumer shape.
    /// </summary>
    private static void AssertGeneratedOutputCompiles(Compilation updatedCompilation)
    {
        var errors = updatedCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        errors.Should().BeEmpty(
            "generated output must always compile, but got: {0}",
            string.Join("; ", errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// Compiles sources into an in-memory assembly and returns it as a metadata reference,
    /// for tests that need consumer types split across a referenced "contracts" assembly.
    /// </summary>
    private static MetadataReference EmitToMetadataReference(string assemblyName, params string[] sources)
    {
        var compilation = CreateCompilation(sources).WithAssemblyName(assemblyName);
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        emitResult.Success.Should().BeTrue(
            "the contracts assembly must compile, but got: {0}",
            string.Join("; ", emitResult.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static CSharpCompilation CreateCompilation(params string[] sources)
        => CreateCompilation(sources, extraReferences: []);

    private static CSharpCompilation CreateCompilation(string[] sources, MetadataReference[] extraReferences)
    {
        var referencePaths = new HashSet<string>(
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(static path => !string.IsNullOrEmpty(path)))
        {
            typeof(IMediator).Assembly.Location,
        };

        // Mirror the SDK's ImplicitUsings so the inline consumer snippets compile like a
        // real net10.0 project would.
        const string globalUsings = """
            global using System;
            global using System.Collections.Generic;
            global using System.Linq;
            global using System.Threading;
            global using System.Threading.Tasks;
            """;

        return CSharpCompilation.Create(
            "DriverTests",
            sources.Append(globalUsings).Select(static source => CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))),
            referencePaths.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .Concat(extraReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
