using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MediatorLite;
using MediatorLite.Generated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

BenchmarkRunner.Run<MediatorBenchmarks>();
BenchmarkRunner.Run<PipelineBenchmarks>();
BenchmarkRunner.Run<NotificationBenchmarks>();
BenchmarkRunner.Run<MultipleBehaviorsBenchmarks>();

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

    public class MediatorLiteValidationBehavior<TRequest, TResponse> : MediatorLite.IPipelineBehavior<TRequest, TResponse>
        where TRequest : MediatorLite.IRequest<TResponse>
    {
        public async ValueTask<TResponse> HandleAsync(
            TRequest request,
            MediatorLite.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken = default)
        {
            // Simulated validation - no actual work but adds to pipeline depth
            return await next();
        }
    }

    public class MediatorLiteMetricsBehavior<TRequest, TResponse> : MediatorLite.IPipelineBehavior<TRequest, TResponse>
        where TRequest : MediatorLite.IRequest<TResponse>
    {
        public async ValueTask<TResponse> HandleAsync(
            TRequest request,
            MediatorLite.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken = default)
        {
            // Simulated metrics collection
            return await next();
        }
    }

    // Notification types - Sequential (library default, no attribute)
    public record MediatorLiteNotification(int Id) : MediatorLite.INotification;

    public class MediatorLiteNotificationHandler1 : MediatorLite.INotificationHandler<MediatorLiteNotification>
    {
        public ValueTask HandleAsync(MediatorLiteNotification notification, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    public class MediatorLiteNotificationHandler2 : MediatorLite.INotificationHandler<MediatorLiteNotification>
    {
        public ValueTask HandleAsync(MediatorLiteNotification notification, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    public class MediatorLiteNotificationHandler3 : MediatorLite.INotificationHandler<MediatorLiteNotification>
    {
        public ValueTask HandleAsync(MediatorLiteNotification notification, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    // Notification types - Parallel (compile-time attribute)
    [MediatorLite.NotificationExecution(MediatorLite.NotificationExecutionStrategy.Parallel)]
    public record MediatorLiteNotificationParallel(int Id) : MediatorLite.INotification;

    public class MediatorLiteNotificationParallelHandler1 : MediatorLite.INotificationHandler<MediatorLiteNotificationParallel>
    {
        public ValueTask HandleAsync(MediatorLiteNotificationParallel notification, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    public class MediatorLiteNotificationParallelHandler2 : MediatorLite.INotificationHandler<MediatorLiteNotificationParallel>
    {
        public ValueTask HandleAsync(MediatorLiteNotificationParallel notification, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    public class MediatorLiteNotificationParallelHandler3 : MediatorLite.INotificationHandler<MediatorLiteNotificationParallel>
    {
        public ValueTask HandleAsync(MediatorLiteNotificationParallel notification, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
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

    public class MediatRValidationBehavior<TRequest, TResponse> : MediatR.IPipelineBehavior<TRequest, TResponse>
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

    public class MediatRMetricsBehavior<TRequest, TResponse> : MediatR.IPipelineBehavior<TRequest, TResponse>
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

    // MediatR Notification types
    public record MediatRNotification(int Id) : MediatR.INotification;

    public class MediatRNotificationHandler1 : MediatR.INotificationHandler<MediatRNotification>
    {
        public Task Handle(MediatRNotification notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public class MediatRNotificationHandler2 : MediatR.INotificationHandler<MediatRNotification>
    {
        public Task Handle(MediatRNotification notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public class MediatRNotificationHandler3 : MediatR.INotificationHandler<MediatRNotification>
    {
        public Task Handle(MediatRNotification notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    #endregion

    [GlobalSetup]
    public void Setup()
    {
        // Setup MediatorLite with v2 source-gen dispatch
        // AddGeneratedHandlers() auto-registers all discovered handlers, behaviors, and SourceGeneratedMediator
        var mediatorLiteServices = new ServiceCollection();
        mediatorLiteServices.AddGeneratedHandlers();
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

// Benchmark with single pipeline behavior
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
        // Setup MediatorLite with v2 source-gen dispatch (behaviors auto-registered)
        var mediatorLiteServices = new ServiceCollection();
        mediatorLiteServices.AddGeneratedHandlers();
        mediatorLiteServices.AddMediatorLite(options =>
        {
            options.EnableBuiltInLogging = false;
            options.EnableTracing = false;
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

// Benchmark with multiple pipeline behaviors (realistic production scenario)
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MultipleBehaviorsBenchmarks
{
    private IServiceProvider _mediatorLiteProvider = null!;
    private IServiceProvider _mediatrProvider = null!;
    private MediatorLite.IMediator _mediatorLite = null!;
    private MediatR.IMediator _mediatr = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Setup MediatorLite with v2 source-gen dispatch (all behaviors auto-registered)
        var mediatorLiteServices = new ServiceCollection();
        mediatorLiteServices.AddGeneratedHandlers();
        mediatorLiteServices.AddMediatorLite(options =>
        {
            options.EnableBuiltInLogging = false;
            options.EnableTracing = false;
        });
        mediatorLiteServices.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        mediatorLiteServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _mediatorLiteProvider = mediatorLiteServices.BuildServiceProvider();
        _mediatorLite = _mediatorLiteProvider.GetRequiredService<MediatorLite.IMediator>();

        // Setup MediatR with 3 behaviors
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<MediatorBenchmarks>();
            cfg.AddOpenBehavior(typeof(MediatorBenchmarks.MediatRLoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(MediatorBenchmarks.MediatRValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(MediatorBenchmarks.MediatRMetricsBehavior<,>));
        });
        _mediatrProvider = mediatrServices.BuildServiceProvider();
        _mediatr = _mediatrProvider.GetRequiredService<MediatR.IMediator>();
    }

    [Benchmark(Baseline = true)]
    public async Task<MediatorBenchmarks.MediatRResult> MediatR_WithMultipleBehaviors()
    {
        return await _mediatr.Send(new MediatorBenchmarks.MediatRQuery(1));
    }

    [Benchmark]
    public async Task<MediatorBenchmarks.MediatorLiteResult> MediatorLite_WithMultipleBehaviors()
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

// Benchmark notifications (sequential and parallel)
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class NotificationBenchmarks
{
    private IServiceProvider _mediatorLiteProvider = null!;
    private IServiceProvider _mediatrProvider = null!;
    private MediatorLite.IMediator _mediatorLite = null!;
    private MediatR.IMediator _mediatr = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Single MediatorLite provider — strategies are compile-time per notification type:
        //   MediatorLiteNotification         → Sequential (library default, no attribute)
        //   MediatorLiteNotificationParallel → Parallel ([NotificationExecution(Parallel)])
        var mediatorLiteServices = new ServiceCollection();
        mediatorLiteServices.AddGeneratedHandlers();
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
    public async Task MediatR_Notification()
    {
        await _mediatr.Publish(new MediatorBenchmarks.MediatRNotification(1));
    }

    [Benchmark]
    public async Task MediatorLite_Sequential_Notification()
    {
        await _mediatorLite.PublishAsync(new MediatorBenchmarks.MediatorLiteNotification(1));
    }

    [Benchmark]
    public async Task MediatorLite_Parallel_Notification()
    {
        await _mediatorLite.PublishAsync(new MediatorBenchmarks.MediatorLiteNotificationParallel(1));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_mediatorLiteProvider as IDisposable)?.Dispose();
        (_mediatrProvider as IDisposable)?.Dispose();
    }
}