using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using MediatorLite.Generated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CompetitiveBenchmarks.DefaultDiagnostics;

public sealed record Result(int Value);

public sealed record MlQuery(int Id) : MediatorLite.IRequest<Result>;

public sealed class MlQueryHandler : MediatorLite.IRequestHandler<MlQuery, Result>
{
    public ValueTask<Result> HandleAsync(MlQuery request, CancellationToken cancellationToken = default)
        => new(new Result(request.Id));
}

public sealed record MlNotification(int Id) : MediatorLite.INotification;

public sealed class MlNotificationHandler1 : MediatorLite.INotificationHandler<MlNotification>
{
    public ValueTask HandleAsync(MlNotification n, CancellationToken ct = default) => default;
}
public sealed class MlNotificationHandler2 : MediatorLite.INotificationHandler<MlNotification>
{
    public ValueTask HandleAsync(MlNotification n, CancellationToken ct = default) => default;
}
public sealed class MlNotificationHandler3 : MediatorLite.INotificationHandler<MlNotification>
{
    public ValueTask HandleAsync(MlNotification n, CancellationToken ct = default) => default;
}

public sealed record MrQuery(int Id) : MediatR.IRequest<Result>;

public sealed class MrQueryHandler : MediatR.IRequestHandler<MrQuery, Result>
{
    public Task<Result> Handle(MrQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new Result(request.Id));
}

/// <summary>
/// MediatorLite in its DEFAULT configuration: inline logging + tracing emitted by the
/// source generator (no [assembly: DisableMediatorLogging] / DisableMediatorTracing).
/// Three logger configurations are measured, plus a direct handler-call floor.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 5, iterationCount: 15)]
public class DefaultDiagnosticsBench
{
    private ServiceProvider _nullSp = null!, _filteredSp = null!;
    private IServiceScope _nullScope = null!, _filteredScope = null!;
    private MediatorLite.IMediator _null = null!, _filtered = null!;
    private ServiceProvider _mrSp = null!;
    private IServiceScope _mrScope = null!;
    private MediatR.IMediator _mr = null!;
    private readonly MrQuery _mrMsg = new(1);
    private readonly MlQueryHandler _direct = new();
    private readonly MlQuery _msg = new(1);
    private readonly MlNotification _note = new(1);

    private static ServiceProvider Build(Action<ILoggingBuilder> cfg)
    {
        var s = new ServiceCollection();
        if (cfg is null)
        {
            s.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            s.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        }
        else
        {
            s.AddLogging(cfg);
        }
        MediatorLite.ServiceCollectionExtensions.AddMediatorLite(s.AddGeneratedHandlers());
        return s.BuildServiceProvider();
    }

    [GlobalSetup]
    public void Setup()
    {
        _nullSp = Build(null);
        // A realistic production logger: real ILogger implementation, Debug filtered out.
        _filteredSp = Build(b => b.SetMinimumLevel(LogLevel.Information));
        // MediatR reference point, resolved in the same assembly and process.
        var mrServices = new ServiceCollection();
        mrServices.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        mrServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        mrServices.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DefaultDiagnosticsBench).Assembly));
        _mrSp = mrServices.BuildServiceProvider();
        _mrScope = _mrSp.CreateScope();
        _mr = _mrScope.ServiceProvider.GetRequiredService<MediatR.IMediator>();

        _nullScope = _nullSp.CreateScope();
        _filteredScope = _filteredSp.CreateScope();
        _null = _nullScope.ServiceProvider.GetRequiredService<MediatorLite.IMediator>();
        _filtered = _filteredScope.ServiceProvider.GetRequiredService<MediatorLite.IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _nullScope.Dispose(); _filteredScope.Dispose(); _mrScope.Dispose();
        _nullSp.Dispose(); _filteredSp.Dispose(); _mrSp.Dispose();
    }

    /// <summary>Absolute floor: call the handler directly, no mediator at all.</summary>
    [Benchmark(Baseline = true)]
    public ValueTask<Result> DirectHandlerCall() => _direct.HandleAsync(_msg);

    [Benchmark]
    public ValueTask<Result> Default_NullLogger() => _null.SendAsync(_msg);

    [Benchmark]
    public ValueTask<Result> Default_RealLoggerDebugFilteredOut() => _filtered.SendAsync(_msg);

    /// <summary>
    /// MediatR has no built-in logging, so this is what MediatorLite's default
    /// configuration is actually competing against out of the box.
    /// </summary>
    [Benchmark]
    public Task<Result> MediatR_ForReference() => _mr.Send(_mrMsg);

    [Benchmark]
    public ValueTask Default_Publish3_NullLogger() => _null.PublishAsync(_note);
}

public static class Entry
{
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Entry).Assembly).Run(args);
}
