using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using MediatorLite.Generated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CompetitiveBenchmarks.LargeProject;

internal static class Setup
{
    /// <summary>
    /// Null logging on every container so no library pays for real log output.
    /// </summary>
    public static void AddNullLogging(IServiceCollection s)
    {
        s.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        s.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    public static ServiceProvider BuildMediatorLite()
    {
        var s = new ServiceCollection();
        AddNullLogging(s);
        MediatorLite.ServiceCollectionExtensions.AddMediatorLite(s.AddGeneratedHandlers());
        return s.BuildServiceProvider();
    }

    public static ServiceProvider BuildMediator()
    {
        var s = new ServiceCollection();
        AddNullLogging(s);
        s.AddMediator();
        return s.BuildServiceProvider();
    }

    public static ServiceProvider BuildMediatR()
    {
        var s = new ServiceCollection();
        AddNullLogging(s);
        s.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Setup).Assembly));
        s.AddTransient<MediatR.IPipelineBehavior<MrPipelineQuery, Result>, MrBehavior1>();
        s.AddTransient<MediatR.IPipelineBehavior<MrPipelineQuery, Result>, MrBehavior2>();
        s.AddTransient<MediatR.IPipelineBehavior<MrPipelineQuery, Result>, MrBehavior3>();
        return s.BuildServiceProvider();
    }
}

/// <summary>
/// Scenario 1: single request, no pipeline behaviors, synchronously-completing handler.
/// Isolates raw dispatch + handler-resolution overhead.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 15)]
public class SimpleRequestBench
{
    private ServiceProvider _mlSp = null!, _moSp = null!, _mrSp = null!;
    private IServiceScope _mlScope = null!, _moScope = null!, _mrScope = null!;
    private MediatorLite.IMediator _ml = null!;
    private Mediator.IMediator _mo = null!;
    private MediatR.IMediator _mr = null!;

    private readonly MlQuery _mlMsg = new(1);
    private readonly MoQuery _moMsg = new(1);
    private readonly MrQuery _mrMsg = new(1);

    [GlobalSetup]
    public void Setup()
    {
        _mlSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediatorLite();
        _moSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediator();
        _mrSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediatR();
        _mlScope = _mlSp.CreateScope();
        _moScope = _moSp.CreateScope();
        _mrScope = _mrSp.CreateScope();
        _ml = _mlScope.ServiceProvider.GetRequiredService<MediatorLite.IMediator>();
        _mo = _moScope.ServiceProvider.GetRequiredService<Mediator.IMediator>();
        _mr = _mrScope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _mlScope.Dispose(); _moScope.Dispose(); _mrScope.Dispose();
        _mlSp.Dispose(); _moSp.Dispose(); _mrSp.Dispose();
    }

    private readonly MlQueryHandler _direct = new();

    /// <summary>Absolute floor: no mediator, straight handler invocation.</summary>
    [Benchmark]
    public ValueTask<Result> DirectCall() => _direct.HandleAsync(_mlMsg);

    [Benchmark(Baseline = true)]
    public Task<Result> MediatR() => _mr.Send(_mrMsg);

    [Benchmark]
    public ValueTask<Result> MediatorLite() => _ml.SendAsync(_mlMsg);

    [Benchmark]
    public ValueTask<Result> Mediator() => _mo.Send(_moMsg);
}

/// <summary>
/// Scenario 2: request wrapped by three no-op pipeline behaviors.
/// Isolates the cost of the generated behavior chain (closures / delegates / resolutions).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 15)]
public class PipelineBench
{
    private ServiceProvider _mlSp = null!, _moSp = null!, _mrSp = null!;
    private IServiceScope _mlScope = null!, _moScope = null!, _mrScope = null!;
    private MediatorLite.IMediator _ml = null!;
    private Mediator.IMediator _mo = null!;
    private MediatR.IMediator _mr = null!;

    private readonly MlPipelineQuery _mlMsg = new(1);
    private readonly MoPipelineQuery _moMsg = new(1);
    private readonly MrPipelineQuery _mrMsg = new(1);

    [GlobalSetup]
    public void Setup()
    {
        _mlSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediatorLite();
        _moSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediator();
        _mrSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediatR();
        _mlScope = _mlSp.CreateScope(); _moScope = _moSp.CreateScope(); _mrScope = _mrSp.CreateScope();
        _ml = _mlScope.ServiceProvider.GetRequiredService<MediatorLite.IMediator>();
        _mo = _moScope.ServiceProvider.GetRequiredService<Mediator.IMediator>();
        _mr = _mrScope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _mlScope.Dispose(); _moScope.Dispose(); _mrScope.Dispose();
        _mlSp.Dispose(); _moSp.Dispose(); _mrSp.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Task<Result> MediatR_3Behaviors() => _mr.Send(_mrMsg);

    [Benchmark]
    public ValueTask<Result> MediatorLite_3Behaviors() => _ml.SendAsync(_mlMsg);

    [Benchmark]
    public ValueTask<Result> Mediator_3Behaviors() => _mo.Send(_moMsg);
}

/// <summary>
/// Scenario 3: notification fan-out to three synchronously-completing handlers.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 15)]
public class NotificationBench
{
    private ServiceProvider _mlSp = null!, _moSp = null!, _mrSp = null!;
    private IServiceScope _mlScope = null!, _moScope = null!, _mrScope = null!;
    private MediatorLite.IMediator _ml = null!;
    private Mediator.IMediator _mo = null!;
    private MediatR.IMediator _mr = null!;

    private readonly MlNotification _mlMsg = new(1);
    private readonly MoNotification _moMsg = new(1);
    private readonly MrNotification _mrMsg = new(1);

    [GlobalSetup]
    public void Setup()
    {
        _mlSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediatorLite();
        _moSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediator();
        _mrSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediatR();
        _mlScope = _mlSp.CreateScope(); _moScope = _moSp.CreateScope(); _mrScope = _mrSp.CreateScope();
        _ml = _mlScope.ServiceProvider.GetRequiredService<MediatorLite.IMediator>();
        _mo = _moScope.ServiceProvider.GetRequiredService<Mediator.IMediator>();
        _mr = _mrScope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _mlScope.Dispose(); _moScope.Dispose(); _mrScope.Dispose();
        _mlSp.Dispose(); _moSp.Dispose(); _mrSp.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Task MediatR_Publish3() => _mr.Publish(_mrMsg);

    [Benchmark]
    public ValueTask MediatorLite_Publish3() => _ml.PublishAsync(_mlMsg);

    [Benchmark]
    public ValueTask Mediator_Publish3() => _mo.Publish(_moMsg);
}

/// <summary>
/// Scenario 4: the realistic ASP.NET Core per-request shape — open a DI scope,
/// resolve the mediator, send one message, dispose the scope.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 15)]
public class ScopedRequestBench
{
    private ServiceProvider _mlSp = null!, _moSp = null!, _mrSp = null!;
    private readonly MlQuery _mlMsg = new(1);
    private readonly MoQuery _moMsg = new(1);
    private readonly MrQuery _mrMsg = new(1);

    [GlobalSetup]
    public void Setup()
    {
        _mlSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediatorLite();
        _moSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediator();
        _mrSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediatR();
    }

    [GlobalCleanup]
    public void Cleanup() { _mlSp.Dispose(); _moSp.Dispose(); _mrSp.Dispose(); }

    [Benchmark(Baseline = true)]
    public async Task<Result> MediatR_Scoped()
    {
        using var scope = _mrSp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>().Send(_mrMsg);
    }

    [Benchmark]
    public async Task<Result> MediatorLite_Scoped()
    {
        using var scope = _mlSp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MediatorLite.IMediator>().SendAsync(_mlMsg);
    }

    [Benchmark]
    public async Task<Result> Mediator_Scoped()
    {
        using var scope = _moSp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Mediator.IMediator>().Send(_moMsg);
    }
}

/// <summary>
/// Scenario 5: does dispatch cost depend on WHERE a message type sits in the generated
/// dispatch structure? MediatorLite emits a sequential type-pattern switch; a linear scan
/// would make the last arm measurably slower than the first. 64 request types are
/// registered (see ScaleMessages.cs).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 15)]
public class DispatchScalingBench
{
    private ServiceProvider _mlSp = null!, _moSp = null!;
    private IServiceScope _mlScope = null!, _moScope = null!;
    private MediatorLite.IMediator _ml = null!;
    private Mediator.IMediator _mo = null!;

    private readonly Scale.MlScale00 _mlFirst = new(1);
    private readonly Scale.MlScale63 _mlLast = new(1);
    private readonly Scale.MoScale00 _moFirst = new(1);
    private readonly Scale.MoScale63 _moLast = new(1);

    [GlobalSetup]
    public void Setup()
    {
        _mlSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediatorLite();
        _moSp = CompetitiveBenchmarks.LargeProject.Setup.BuildMediator();
        _mlScope = _mlSp.CreateScope(); _moScope = _moSp.CreateScope();
        _ml = _mlScope.ServiceProvider.GetRequiredService<MediatorLite.IMediator>();
        _mo = _moScope.ServiceProvider.GetRequiredService<Mediator.IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup() { _mlScope.Dispose(); _moScope.Dispose(); _mlSp.Dispose(); _moSp.Dispose(); }

    [Benchmark(Baseline = true)]
    public ValueTask<Result> MediatorLite_FirstOfN() => _ml.SendAsync(_mlFirst);

    [Benchmark]
    public ValueTask<Result> MediatorLite_LastOfN() => _ml.SendAsync(_mlLast);

    [Benchmark]
    public ValueTask<Result> Mediator_FirstOfN() => _mo.Send(_moFirst);

    [Benchmark]
    public ValueTask<Result> Mediator_LastOfN() => _mo.Send(_moLast);
}
