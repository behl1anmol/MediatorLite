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

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var referencePaths = new HashSet<string>(
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Where(static path => !string.IsNullOrEmpty(path)))
        {
            typeof(IMediator).Assembly.Location,
        };

        return CSharpCompilation.Create(
            "DriverTests",
            sources.Select(static source => CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))),
            referencePaths.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
