using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using MediatorLite.Generated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CompetitiveBenchmarks.SmallProject;

public sealed record Result(int Value);

public sealed record MlQuery(int Id) : MediatorLite.IRequest<Result>;
public sealed class MlQueryHandler : MediatorLite.IRequestHandler<MlQuery, Result>
{
    public ValueTask<Result> HandleAsync(MlQuery r, CancellationToken ct = default) => new(new Result(r.Id));
}

public sealed record MoQuery(int Id) : Mediator.IRequest<Result>;
public sealed class MoQueryHandler : Mediator.IRequestHandler<MoQuery, Result>
{
    public ValueTask<Result> Handle(MoQuery r, CancellationToken ct) => new(new Result(r.Id));
}

public sealed record MrQuery(int Id) : MediatR.IRequest<Result>;
public sealed class MrQueryHandler : MediatR.IRequestHandler<MrQuery, Result>
{
    public Task<Result> Handle(MrQuery r, CancellationToken ct) => Task.FromResult(new Result(r.Id));
}

/// <summary>
/// "Small project" shape (1 request type per library) — matches the message-count
/// regime both MediatorLite's and Mediator's own published benchmarks run in.
/// MediatorLite diagnostics are OFF (see AssemblyInfo.cs).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 15)]
public class SmallProjectBench
{
    private ServiceProvider _mlSp = null!, _moSp = null!, _mrSp = null!, _mlSingletonSp = null!;
    private IServiceScope _mlScope = null!, _moScope = null!, _mrScope = null!, _mlSingletonScope = null!;
    private MediatorLite.IMediator _ml = null!;
    private MediatorLite.IMediator _mlSingleton = null!;
    private Mediator.IMediator _mo = null!;
    private Mediator.Mediator _moConcrete = null!;
    private MediatR.IMediator _mr = null!;
    private readonly MlQueryHandler _direct = new();

    private readonly MlQuery _mlMsg = new(1);
    private readonly MoQuery _moMsg = new(1);
    private readonly MrQuery _mrMsg = new(1);

    [GlobalSetup]
    public void Setup()
    {
        var s1 = new ServiceCollection();
        s1.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        s1.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        MediatorLite.ServiceCollectionExtensions.AddMediatorLite(s1.AddGeneratedHandlers());
        _mlSp = s1.BuildServiceProvider();

        // Same generated dispatch path, but the handler registration is overridden to
        // Singleton AFTER AddGeneratedHandlers(). MS.DI resolves the last descriptor, so
        // this isolates how much of MediatorLite's per-send cost is the Transient handler
        // lifetime versus the dispatch machinery itself.
        var s1b = new ServiceCollection();
        s1b.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        s1b.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        MediatorLite.ServiceCollectionExtensions.AddMediatorLite(s1b.AddGeneratedHandlers());
        s1b.AddSingleton<MediatorLite.IRequestHandler<MlQuery, Result>, MlQueryHandler>();
        _mlSingletonSp = s1b.BuildServiceProvider();

        var s2 = new ServiceCollection();
        s2.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        s2.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        s2.AddMediator();
        _moSp = s2.BuildServiceProvider();

        var s3 = new ServiceCollection();
        s3.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        s3.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        s3.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SmallProjectBench).Assembly));
        _mrSp = s3.BuildServiceProvider();

        _mlScope = _mlSp.CreateScope(); _moScope = _moSp.CreateScope(); _mrScope = _mrSp.CreateScope();
        _mlSingletonScope = _mlSingletonSp.CreateScope();
        _mlSingleton = _mlSingletonScope.ServiceProvider.GetRequiredService<MediatorLite.IMediator>();
        _ml = _mlScope.ServiceProvider.GetRequiredService<MediatorLite.IMediator>();
        _mo = _moScope.ServiceProvider.GetRequiredService<Mediator.IMediator>();
        _moConcrete = _moScope.ServiceProvider.GetRequiredService<Mediator.Mediator>();
        _mr = _mrScope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _mlScope.Dispose(); _moScope.Dispose(); _mrScope.Dispose(); _mlSingletonScope.Dispose();
        _mlSp.Dispose(); _moSp.Dispose(); _mrSp.Dispose(); _mlSingletonSp.Dispose();
    }

    [Benchmark]
    public ValueTask<Result> DirectCall() => _direct.HandleAsync(_mlMsg);

    [Benchmark(Baseline = true)]
    public Task<Result> MediatR_IMediator() => _mr.Send(_mrMsg);

    [Benchmark]
    public ValueTask<Result> MediatorLite_IMediator() => _ml.SendAsync(_mlMsg);

    /// <summary>MediatorLite's generated dispatch with the handler forced to Singleton.</summary>
    [Benchmark]
    public ValueTask<Result> MediatorLite_IMediator_SingletonHandler() => _mlSingleton.SendAsync(_mlMsg);

    [Benchmark]
    public ValueTask<Result> Mediator_IMediator() => _mo.Send(_moMsg);

    /// <summary>Mediator's monomorphized concrete-class overload (no MediatorLite equivalent exists).</summary>
    [Benchmark]
    public ValueTask<Result> Mediator_ConcreteClass() => _moConcrete.Send(_moMsg);
}

/// <summary>Container build + first-resolve cost (cold start / startup).</summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 10)]
public class ColdStartBench
{
    private readonly MlQuery _mlMsg = new(1);
    private readonly MoQuery _moMsg = new(1);
    private readonly MrQuery _mrMsg = new(1);

    [Benchmark(Baseline = true)]
    public async Task<Result> MediatR_ColdStart()
    {
        var s = new ServiceCollection();
        s.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        s.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        s.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ColdStartBench).Assembly));
        await using var sp = s.BuildServiceProvider();
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MediatR.IMediator>().Send(_mrMsg);
    }

    [Benchmark]
    public async Task<Result> MediatorLite_ColdStart()
    {
        var s = new ServiceCollection();
        s.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        s.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        MediatorLite.ServiceCollectionExtensions.AddMediatorLite(s.AddGeneratedHandlers());
        await using var sp = s.BuildServiceProvider();
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MediatorLite.IMediator>().SendAsync(_mlMsg);
    }

    [Benchmark]
    public async Task<Result> Mediator_ColdStart()
    {
        var s = new ServiceCollection();
        s.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        s.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        s.AddMediator();
        await using var sp = s.BuildServiceProvider();
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<Mediator.IMediator>().Send(_moMsg);
    }
}

public static class Entry
{
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Entry).Assembly).Run(args);
}
