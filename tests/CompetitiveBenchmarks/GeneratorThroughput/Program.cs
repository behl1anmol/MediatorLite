using System.Diagnostics;
using MediatorLite.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Measures HandlerDiscoveryGenerator throughput: cold run cost vs handler count, and
// whether an unrelated edit is served from the incremental cache.

static string BuildSource(int n)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("using MediatorLite;");
    sb.AppendLine("namespace GenBench;");
    for (int i = 0; i < n; i++)
    {
        sb.AppendLine($"public record Q{i}(int V) : IRequest<int>;");
        sb.AppendLine($"public sealed class Q{i}Handler : IRequestHandler<Q{i}, int> {{ public ValueTask<int> HandleAsync(Q{i} r, CancellationToken ct = default) => ValueTask.FromResult(r.V); }}");
        sb.AppendLine($"public record N{i}(int V) : INotification;");
        sb.AppendLine($"public sealed class N{i}Handler : INotificationHandler<N{i}> {{ public ValueTask HandleAsync(N{i} n, CancellationToken ct = default) => default; }}");
    }
    return sb.ToString();
}

static CSharpCompilation CreateCompilation(string source)
{
    var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(p => !string.IsNullOrEmpty(p))
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToList();
    refs.Add(MetadataReference.CreateFromFile(typeof(MediatorLite.IMediator).Assembly.Location));
    refs.Add(MetadataReference.CreateFromFile(typeof(MediatorLite.IRequest<>).Assembly.Location));
    return CSharpCompilation.Create(
        "GenBenchAsm",
        [CSharpSyntaxTree.ParseText(source)],
        refs,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
}

Console.WriteLine("| Handlers | Cold run (ms) | Warm rerun, unrelated edit (ms) | Output re-executed? |");
Console.WriteLine("|---------:|--------------:|--------------------------------:|---------------------|");

foreach (var n in new[] { 25, 100, 400 })
{
    var compilation = CreateCompilation(BuildSource(n));

    // Warm up Roslyn + the generator so we time the generator, not first-call JIT.
    _ = CSharpGeneratorDriver
        .Create([new HandlerDiscoveryGenerator().AsSourceGenerator()],
                driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true))
        .RunGenerators(compilation);

    var driver = CSharpGeneratorDriver.Create(
        [new HandlerDiscoveryGenerator().AsSourceGenerator()],
        driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

    var sw = Stopwatch.StartNew();
    driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
    sw.Stop();
    var cold = sw.Elapsed.TotalMilliseconds;

    // Add a syntax tree that contains no MediatorLite types at all. A well-cached
    // generator should serve the output node from cache (Cached/Unchanged).
    var edited = compilation.AddSyntaxTrees(
        CSharpSyntaxTree.ParseText("namespace Unrelated; public class Irrelevant { public int X => 42; }"));

    sw.Restart();
    var driver2 = (CSharpGeneratorDriver)driver.RunGenerators(edited);
    sw.Stop();
    var warm = sw.Elapsed.TotalMilliseconds;

    var result = driver2.GetRunResult().Results[0];
    var reasons = result.TrackedOutputSteps
        .SelectMany(kv => kv.Value)
        .SelectMany(s => s.Outputs)
        .Select(o => o.Reason)
        .Distinct()
        .ToList();
    var reExecuted = reasons.Any(r => r is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);

    Console.WriteLine($"| {n * 2} | {cold:F1} | {warm:F1} | {(reExecuted ? "YES (" : "no (")}{string.Join(",", reasons)}) |");
}
