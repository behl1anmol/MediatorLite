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

// A single Stopwatch sample is far too noisy to compare sizes against each other
// (observed: a "warm" sample beating the cold one). Report the median of N runs.
const int Samples = 7;

static double Median(List<double> xs)
{
    xs.Sort();
    return xs.Count % 2 == 1 ? xs[xs.Count / 2] : (xs[xs.Count / 2 - 1] + xs[xs.Count / 2]) / 2.0;
}

Console.WriteLine($"Median of {Samples} runs per cell.");
Console.WriteLine();
Console.WriteLine("| Handlers | Cold run (ms) | Rerun after unrelated edit (ms) | Output re-executed? |");
Console.WriteLine("|---------:|--------------:|--------------------------------:|---------------------|");

foreach (var n in new[] { 25, 100, 400 })
{
    var compilation = CreateCompilation(BuildSource(n));

    // Warm up Roslyn + the generator so we time the generator, not first-call JIT.
    _ = CSharpGeneratorDriver
        .Create([new HandlerDiscoveryGenerator().AsSourceGenerator()],
                driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true))
        .RunGenerators(compilation);

    // A syntax tree containing no MediatorLite types at all. A well-cached generator
    // should serve the output node from cache rather than re-running Execute.
    var edited = compilation.AddSyntaxTrees(
        CSharpSyntaxTree.ParseText("namespace Unrelated; public class Irrelevant { public int X => 42; }"));

    var colds = new List<double>();
    var warms = new List<double>();
    var allReasons = new HashSet<IncrementalStepRunReason>();

    for (int sample = 0; sample < Samples; sample++)
    {
        var driver = CSharpGeneratorDriver.Create(
            [new HandlerDiscoveryGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        var sw = Stopwatch.StartNew();
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        sw.Stop();
        colds.Add(sw.Elapsed.TotalMilliseconds);

        sw.Restart();
        var driver2 = (CSharpGeneratorDriver)driver.RunGenerators(edited);
        sw.Stop();
        warms.Add(sw.Elapsed.TotalMilliseconds);

        foreach (var reason in driver2.GetRunResult().Results[0].TrackedOutputSteps
                     .SelectMany(kv => kv.Value)
                     .SelectMany(step => step.Outputs)
                     .Select(o => o.Reason))
        {
            allReasons.Add(reason);
        }
    }

    var reExecuted = allReasons.Any(r => r is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);

    Console.WriteLine($"| {n * 2} | {Median(colds):F1} | {Median(warms):F1} | {(reExecuted ? "YES (" : "no (")}{string.Join(",", allReasons)}) |");
}
