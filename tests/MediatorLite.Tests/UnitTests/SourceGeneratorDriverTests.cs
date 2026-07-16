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
    public void GenericHandlerClass_WithClosedInterface_ReportsMedl1004_AndIsNotEmitted()
    {
        // The class is generic but the IRequestHandler<,> interface is fully closed. The
        // class's fully-qualified display name (`UnusedParamHandler<TUnused>`) would be pasted
        // verbatim into the generated registration, where TUnused is an unknown identifier
        // (CS0246) — the consumer's build breaks inside a .g.cs file.
        const string handlerSource = """
            using MediatorLite;

            namespace DriverTests;

            public sealed class UnusedParamHandler<TUnused> : IRequestHandler<PingQuery, int>
            {
                public ValueTask<int> HandleAsync(PingQuery request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(request.Value);
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, handlerSource);

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "MEDL1004")
            .Which.GetMessage().Should().Contain("UnusedParamHandler");

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().NotContain("UnusedParamHandler",
            "a generic handler class must be skipped, not emitted as non-compiling code");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void OpenGenericHandlers_ReportMedl1004_AndAreNotEmitted()
    {
        // Open generic handlers (the MediatR pattern) cannot be closed by this generator:
        // the interface type arguments are the class's own type parameters, so emission
        // would produce `case TReq r_TReq:` and registrations full of unbound identifiers.
        const string handlerSource = """
            using MediatorLite;

            namespace DriverTests;

            public sealed class OpenRequestHandler<TReq, TRes> : IRequestHandler<TReq, TRes>
                where TReq : IRequest<TRes>
            {
                public ValueTask<TRes> HandleAsync(TReq request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(default(TRes)!);
            }

            public sealed class OpenNotificationHandler<TNotification> : INotificationHandler<TNotification>
                where TNotification : INotification
            {
                public ValueTask HandleAsync(TNotification notification, CancellationToken cancellationToken = default)
                    => ValueTask.CompletedTask;
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, handlerSource);

        var medl1004 = runResult.Diagnostics.Where(d => d.Id == "MEDL1004").ToList();
        medl1004.Should().HaveCount(2);
        medl1004.Select(d => d.GetMessage()).Should()
            .Contain(m => m.Contains("OpenRequestHandler"))
            .And.Contain(m => m.Contains("OpenNotificationHandler"));

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().NotContain("OpenRequestHandler").And.NotContain("OpenNotificationHandler",
            "open generic handlers must be skipped, not emitted as non-compiling code");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void HandlerNestedInGenericOuterType_ReportsMedl1004_AndIsNotEmitted()
    {
        // The handler itself is non-generic, but its containing type is generic, so its
        // fully-qualified display name (`Outer<T>.InnerHandler`) still contains an unbound
        // type parameter when emitted.
        const string handlerSource = """
            using MediatorLite;

            namespace DriverTests;

            public static class Outer<T>
            {
                public sealed class InnerHandler : IRequestHandler<PingQuery, int>
                {
                    public ValueTask<int> HandleAsync(PingQuery request, CancellationToken cancellationToken = default)
                        => ValueTask.FromResult(request.Value);
                }
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, handlerSource);

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "MEDL1004")
            .Which.GetMessage().Should().Contain("InnerHandler");

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().NotContain("InnerHandler",
            "a handler nested inside a generic type must be skipped, not emitted as non-compiling code");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void BehaviorNestedInGenericOuterType_ReportsMedl1002_AndIsNotEmitted()
    {
        // Same nesting hole for behaviors: the class's own type-parameter list is empty, so
        // the pre-existing closed-interface check missed it, but the display name
        // (`Outer<T>.InnerBehavior`) still carries the outer type's parameter.
        const string behaviorSource = """
            using MediatorLite;

            namespace DriverTests;

            public static class BehaviorOuter<T>
            {
                public sealed class InnerBehavior : IPipelineBehavior<PingQuery, int>
                {
                    public ValueTask<int> HandleAsync(
                        PingQuery request,
                        RequestHandlerDelegate<int> next,
                        CancellationToken cancellationToken = default)
                        => next();
                }
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, behaviorSource);

        runResult.Diagnostics.Should().ContainSingle(d => d.Id == "MEDL1002")
            .Which.GetMessage().Should().Contain("InnerBehavior");

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().NotContain("InnerBehavior",
            "a behavior nested inside a generic type must be skipped, not emitted as non-compiling code");

        AssertGeneratedOutputCompiles(updatedCompilation);
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
    public void NotificationTypes_WhoseSanitizedNamesCollide_StillGenerateCompilableDispatch()
    {
        // Same collision as the request-type case above, but for notifications: the switch
        // arms use the uniquified suffix (PublishAsync's `case ... : return Publish_{suffix}`),
        // but the unrolled Publish_* method bodies must be emitted under that same uniquified
        // suffix too — a prior fix threaded the suffix into the switch but re-derived the
        // publisher method name with plain GetSafeTypeName, so the switch called a
        // Publish_*_2 method that was never emitted while the real Publish_* methods still
        // collided (CS0111 and a dangling call to a non-existent method).
        const string source = """
            using MediatorLite;

            namespace App
            {
                public record User_Created(int Id) : INotification;

                public sealed class User_CreatedHandler : INotificationHandler<User_Created>
                {
                    public ValueTask HandleAsync(User_Created notification, CancellationToken cancellationToken = default)
                        => ValueTask.CompletedTask;
                }
            }

            namespace App.User
            {
                public record Created(int Id) : INotification;

                public sealed class CreatedHandler : INotificationHandler<Created>
                {
                    public ValueTask HandleAsync(Created notification, CancellationToken cancellationToken = default)
                        => ValueTask.CompletedTask;
                }
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(source);

        var mediator = runResult.GeneratedTrees
            .Select(t => t.ToString())
            .Single(t => t.Contains("class SourceGeneratedMediator"));

        mediator.Should().Contain("case global::App.User_Created")
            .And.Contain("case global::App.User.Created", "both notification types must keep their own dispatch arm");

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
    public void ObsoleteMediatorGenerationSkip_IsIgnored_HandlerIsStillDiscovered()
    {
        // v2 discovery is unconditional (rule 70 §3). The obsolete
        // [MediatorGeneration(Skip = true)] must have no effect on the generator; consumers
        // who need to exclude a type move it to a non-compiled assembly instead.
        const string source = """
            using MediatorLite;

            namespace DriverTests;

            public record SkippedQuery(int Value) : IRequest<int>;

            #pragma warning disable CS0618 // MediatorGenerationAttribute is obsolete
            [MediatorGeneration(Skip = true)]
            #pragma warning restore CS0618
            public sealed class SkippedQueryHandler : IRequestHandler<SkippedQuery, int>
            {
                public ValueTask<int> HandleAsync(SkippedQuery request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(request.Value);
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, source);

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().Contain("SkippedQueryHandler",
            "discovery is unconditional: the obsolete [MediatorGeneration(Skip = true)] has no effect");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void ClosedBehaviorForUnhandledRequestType_IsNotRegisteredOrCounted()
    {
        // A closed behavior bound to a request type with no handler in the compilation can
        // never be resolved by any generated pipeline. It used to be registered in DI and
        // counted in BehaviorCount anyway (dead wiring), while validators in the same
        // situation were filtered out — the policies now match.
        const string source = """
            using MediatorLite;

            namespace DriverTests;

            public record RetiredCommand(int Id) : IRequest<int>;

            public sealed class RetiredAuditBehavior : IPipelineBehavior<RetiredCommand, int>
            {
                public ValueTask<int> HandleAsync(
                    RetiredCommand request,
                    RequestHandlerDelegate<int> next,
                    CancellationToken cancellationToken = default)
                    => next();
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, source);

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().NotContain("RetiredAuditBehavior",
            "a closed behavior whose request type has no handler can never run and must not be wired");
        generated.Should().Contain("public static int BehaviorCount => 0;");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void ValidatorImplementingMultipleValidatorInterfaces_WiresValidationForEveryRequestType()
    {
        // One validator class may validate several request types (IValidator<A> +
        // IValidator<B>). Discovery used to stop at the first matching interface, so B's
        // requests reached their handler unvalidated — silently, with no diagnostic.
        const string source = """
            using FluentValidation;
            using FluentValidation.Results;
            using MediatorLite;

            namespace DriverTests;

            public record CreateOrder(string Name) : IRequest<int>;
            public record UpdateOrder(string Name) : IRequest<int>;

            public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, int>
            {
                public ValueTask<int> HandleAsync(CreateOrder request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(1);
            }

            public sealed class UpdateOrderHandler : IRequestHandler<UpdateOrder, int>
            {
                public ValueTask<int> HandleAsync(UpdateOrder request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(2);
            }

            public sealed class SharedRulesValidator : AbstractValidator<CreateOrder>, IValidator<UpdateOrder>
            {
                public ValidationResult Validate(UpdateOrder instance) => new();

                public Task<ValidationResult> ValidateAsync(UpdateOrder instance, CancellationToken cancellation = default)
                    => Task.FromResult(new ValidationResult());
            }
            """;

        var compilation = CreateCompilation(
            [source],
            [
                MetadataReference.CreateFromFile(typeof(global::FluentValidation.IValidator).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(global::MediatorLite.FluentValidation.FluentValidationBehavior<,>).Assembly.Location),
            ]);

        var driver = CSharpGeneratorDriver.Create(new HandlerDiscoveryGenerator());
        var ranDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        var runResult = ranDriver.GetRunResult();

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));

        generated.Should().Contain("global::FluentValidation.IValidator<global::DriverTests.CreateOrder>, global::DriverTests.SharedRulesValidator")
            .And.Contain("global::FluentValidation.IValidator<global::DriverTests.UpdateOrder>, global::DriverTests.SharedRulesValidator",
                "the validator must be registered for every IValidator<T> it implements");
        generated.Should().Contain("FluentValidationBehavior<global::DriverTests.CreateOrder, int>")
            .And.Contain("FluentValidationBehavior<global::DriverTests.UpdateOrder, int>",
                "both handled request types have a validator, so both pipelines must validate");
        generated.Should().Contain("public static int ValidatorCount => 2;");

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

    [Fact]
    public void ClassConstrainedOpenBehavior_OverReferenceRequest_IsRegistered_AndReportsNoMedl1002()
    {
        // `where TRequest : class, IRequest<TResponse>` is a common MediatR-style shape. It is
        // satisfiable by every reference-type request, so it must expand (not be rejected via
        // MEDL1002). PingQuery from HandlerSource is a record => reference type.
        const string behaviorSource = """
            using MediatorLite;

            namespace DriverTests;

            public sealed class RefLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                where TRequest : class, IRequest<TResponse>
            {
                public ValueTask<TResponse> HandleAsync(
                    TRequest request,
                    RequestHandlerDelegate<TResponse> next,
                    CancellationToken cancellationToken = default)
                    => next();
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, behaviorSource);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "MEDL1002",
            "a `where TRequest : class` open behavior is expandable and must not be rejected");
        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().Contain("RefLoggingBehavior<global::DriverTests.PingQuery, int>",
            "the class-constrained behavior must expand over the reference-type request");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void ClassConstrainedOpenBehavior_SkipsValueTypeRequests_AndStillCompiles()
    {
        // The same behavior must NOT be expanded over a value-type request: substituting a
        // struct into `where TRequest : class` would emit a CS0452-violating closed type.
        // Expansion filters value-type requests so the behavior applies only to reference
        // requests, and AssertGeneratedOutputCompiles is the hard guard against CS0452.
        const string source = """
            using MediatorLite;

            namespace DriverTests;

            public readonly record struct StructQuery(int Value) : IRequest<int>;

            public sealed class StructQueryHandler : IRequestHandler<StructQuery, int>
            {
                public ValueTask<int> HandleAsync(StructQuery request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(request.Value);
            }

            public sealed class RefOnlyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                where TRequest : class, IRequest<TResponse>
            {
                public ValueTask<TResponse> HandleAsync(
                    TRequest request,
                    RequestHandlerDelegate<TResponse> next,
                    CancellationToken cancellationToken = default)
                    => next();
            }
            """;

        // HandlerSource contributes the reference-type PingQuery; `source` adds the struct request.
        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(HandlerSource, source);

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        generated.Should().Contain("RefOnlyBehavior<global::DriverTests.PingQuery, int>",
            "the class-constrained behavior must expand over the reference-type request");
        generated.Should().NotContain("RefOnlyBehavior<global::DriverTests.StructQuery, int>",
            "a `where TRequest : class` behavior must not be expanded over a value-type request");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void CovariantMultiResponseFallback_EmitsMostDerivedResponseFirst()
    {
        // A single request implementing IRequest<Dog> and IRequest<Puppy> (Puppy : Dog : Animal),
        // dispatched via a common base with no exact handler, falls through to the covariant
        // IsAssignableTo chain. That chain must test the most-derived response (Puppy) before its
        // base (Dog), so the more specific pipeline is preferred over the base one.
        const string source = """
            using MediatorLite;

            namespace DriverTests;

            public class Animal { }
            public class Dog : Animal { }
            public class Puppy : Dog { }

            public record BeastQuery(int Id) : IRequest<Dog>, IRequest<Puppy>;

            public sealed class BeastDogHandler : IRequestHandler<BeastQuery, Dog>
            {
                public ValueTask<Dog> HandleAsync(BeastQuery request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(new Dog());
            }

            public sealed class BeastPuppyHandler : IRequestHandler<BeastQuery, Puppy>
            {
                public ValueTask<Puppy> HandleAsync(BeastQuery request, CancellationToken cancellationToken = default)
                    => ValueTask.FromResult(new Puppy());
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(source);

        var mediator = runResult.GeneratedTrees
            .Select(t => t.ToString())
            .Single(t => t.Contains("class SourceGeneratedMediator"));

        var puppyAssignable = mediator.IndexOf(
            "typeof(global::DriverTests.Puppy).IsAssignableTo(typeof(TResponse))", StringComparison.Ordinal);
        var dogAssignable = mediator.IndexOf(
            "typeof(global::DriverTests.Dog).IsAssignableTo(typeof(TResponse))", StringComparison.Ordinal);
        puppyAssignable.Should().BeGreaterThan(-1);
        dogAssignable.Should().BeGreaterThan(-1);
        puppyAssignable.Should().BeLessThan(dogAssignable,
            "the covariant fallback must emit the most-derived response candidate first");

        AssertGeneratedOutputCompiles(updatedCompilation);
    }

    [Fact]
    public void NotificationHandler_ImplementingSeveralInterfaces_RegistersConcreteTypeOnce()
    {
        // A handler class appears in the discovery list once per INotificationHandler<>
        // interface it implements. The interface registration is correctly emitted per
        // interface, but the concrete-type registration (used by the unrolled Publish_*
        // methods) must be emitted once per class — emitting it per interface added a
        // duplicate ServiceDescriptor for every extra interface.
        const string source = """
            using MediatorLite;

            namespace App
            {
                public record OrderPlaced(int Id) : INotification;
                public record OrderShipped(int Id) : INotification;

                public sealed class AuditHandler : INotificationHandler<OrderPlaced>, INotificationHandler<OrderShipped>
                {
                    public ValueTask HandleAsync(OrderPlaced notification, CancellationToken cancellationToken = default)
                        => ValueTask.CompletedTask;

                    public ValueTask HandleAsync(OrderShipped notification, CancellationToken cancellationToken = default)
                        => ValueTask.CompletedTask;
                }
            }
            """;

        var (runResult, updatedCompilation) = RunGeneratorAndUpdateCompilation(source);

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));

        // One interface registration per implemented interface — both must survive.
        generated.Should().Contain(
            "services.AddTransient<global::MediatorLite.INotificationHandler<global::App.OrderPlaced>, global::App.AuditHandler>();");
        generated.Should().Contain(
            "services.AddTransient<global::MediatorLite.INotificationHandler<global::App.OrderShipped>, global::App.AuditHandler>();");

        // Exactly one concrete registration despite the two interfaces.
        const string concreteRegistration = "services.AddTransient<global::App.AuditHandler>();";
        var concreteRegistrations = generated.Split(concreteRegistration).Length - 1;
        concreteRegistrations.Should().Be(1,
            "the concrete handler type must be registered once per class, not once per implemented interface");

        AssertGeneratedOutputCompiles(updatedCompilation);
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
