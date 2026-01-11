using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MediatorLite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

BenchmarkRunner.Run<MediatorBenchmarks>();

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MediatorBenchmarks
{
    private IServiceProvider _mediatorLiteProvider = null!;
    private IServiceProvider _mediatrProvider = null!;
    private MediatorLite.IMediator _mediatorLite = null!;
    private MediatR.IMediator _mediatr = null!;

    #region MediatorLite Types

    public record MediatorLiteQuery(int Id) : MediatorLite.IRequest<MediatorLiteResult>;
    public record MediatorLiteResult(int Id, string Name);

    public class MediatorLiteHandler : MediatorLite.IRequestHandler<MediatorLiteQuery, MediatorLiteResult>
    {
        public ValueTask<MediatorLiteResult> HandleAsync(MediatorLiteQuery request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new MediatorLiteResult(request.Id, "Test"));
        }
    }

    public class MediatorLiteLoggingBehavior<TRequest, TResponse> : MediatorLite.IPipelineBehavior<TRequest, TResponse>
        where TRequest : MediatorLite.IRequest<TResponse>
    {
        public async ValueTask<TResponse> HandleAsync(
            TRequest request,
            MediatorLite.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken = default)
        {
            return await next();
        }
    }

    #endregion

    #region MediatR Types

    public record MediatRQuery(int Id) : MediatR.IRequest<MediatRResult>;
    public record MediatRResult(int Id, string Name);

    public class MediatRHandler : MediatR.IRequestHandler<MediatRQuery, MediatRResult>
    {
        public Task<MediatRResult> Handle(MediatRQuery request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new MediatRResult(request.Id, "Test"));
        }
    }

    public class MediatRLoggingBehavior<TRequest, TResponse> : MediatR.IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(
            TRequest request,
            MediatR.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            return await next();
        }
    }

    #endregion

    [GlobalSetup]
    public void Setup()
    {
        // Setup MediatorLite
        var mediatorLiteServices = new ServiceCollection();
        mediatorLiteServices.AddTransient<MediatorLite.IRequestHandler<MediatorLiteQuery, MediatorLiteResult>, MediatorLiteHandler>();
        mediatorLiteServices.AddMediatorLite(options =>
        {
            options.EnableBuiltInLogging = false;
            options.EnableTracing = false;
        });
        mediatorLiteServices.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        mediatorLiteServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _mediatorLiteProvider = mediatorLiteServices.BuildServiceProvider();
        _mediatorLite = _mediatorLiteProvider.GetRequiredService<MediatorLite.IMediator>();

        // Setup MediatR
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<MediatorBenchmarks>();
        });
        _mediatrProvider = mediatrServices.BuildServiceProvider();
        _mediatr = _mediatrProvider.GetRequiredService<MediatR.IMediator>();
    }

    [Benchmark(Baseline = true)]
    public async Task<MediatRResult> MediatR_SimpleRequest()
    {
        return await _mediatr.Send(new MediatRQuery(1));
    }

    [Benchmark]
    public async Task<MediatorLiteResult> MediatorLite_SimpleRequest()
    {
        return await _mediatorLite.SendAsync(new MediatorLiteQuery(1));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_mediatorLiteProvider as IDisposable)?.Dispose();
        (_mediatrProvider as IDisposable)?.Dispose();
    }
}

// Benchmark with pipeline behaviors
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class PipelineBenchmarks
{
    private IServiceProvider _mediatorLiteProvider = null!;
    private IServiceProvider _mediatrProvider = null!;
    private MediatorLite.IMediator _mediatorLite = null!;
    private MediatR.IMediator _mediatr = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Setup MediatorLite with behaviors
        var mediatorLiteServices = new ServiceCollection();
        mediatorLiteServices.AddTransient<MediatorLite.IRequestHandler<MediatorBenchmarks.MediatorLiteQuery, MediatorBenchmarks.MediatorLiteResult>, MediatorBenchmarks.MediatorLiteHandler>();
        mediatorLiteServices.AddTransient(typeof(MediatorBenchmarks.MediatorLiteLoggingBehavior<,>));
        mediatorLiteServices.AddMediatorLite(options =>
        {
            options.EnableBuiltInLogging = false;
            options.EnableTracing = false;
            options.AddOpenBehavior(typeof(MediatorBenchmarks.MediatorLiteLoggingBehavior<,>));
        });
        mediatorLiteServices.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        mediatorLiteServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _mediatorLiteProvider = mediatorLiteServices.BuildServiceProvider();
        _mediatorLite = _mediatorLiteProvider.GetRequiredService<MediatorLite.IMediator>();

        // Setup MediatR with behaviors
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<MediatorBenchmarks>();
            cfg.AddOpenBehavior(typeof(MediatorBenchmarks.MediatRLoggingBehavior<,>));
        });
        _mediatrProvider = mediatrServices.BuildServiceProvider();
        _mediatr = _mediatrProvider.GetRequiredService<MediatR.IMediator>();
    }

    [Benchmark(Baseline = true)]
    public async Task<MediatorBenchmarks.MediatRResult> MediatR_WithBehavior()
    {
        return await _mediatr.Send(new MediatorBenchmarks.MediatRQuery(1));
    }

    [Benchmark]
    public async Task<MediatorBenchmarks.MediatorLiteResult> MediatorLite_WithBehavior()
    {
        return await _mediatorLite.SendAsync(new MediatorBenchmarks.MediatorLiteQuery(1));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_mediatorLiteProvider as IDisposable)?.Dispose();
        (_mediatrProvider as IDisposable)?.Dispose();
    }
}
