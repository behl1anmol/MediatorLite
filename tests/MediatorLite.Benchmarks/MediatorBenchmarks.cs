using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FluentValidation;
using MediatorLite;
using MediatorLite.Generated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

BenchmarkRunner.Run<MediatorBenchmarks>();
BenchmarkRunner.Run<PipelineBenchmarks>();
BenchmarkRunner.Run<NotificationBenchmarks>();
BenchmarkRunner.Run<MultipleBehaviorsBenchmarks>();
BenchmarkRunner.Run<ValidationBenchmarks>();

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MediatorBenchmarks
{
    private IServiceProvider _mediatorLiteProvider = null!;
    private IServiceProvider _mediatrProvider = null!;
    private MediatorLite.IMediator _mediatorLite = null!;
    private MediatR.IMediator _mediatr = null!;

    #region MediatorLite Types

    // Behaviors are discovered assembly-wide at compile time, and open-generic behaviors are
    // expanded to every request type — which would unfairly add pipeline depth to the
    // zero-behavior scenario. Each scenario therefore gets its own request type, with closed
    // behaviors targeting exactly that type so behavior counts match the MediatR setups:
    //   MediatorLiteQuery       → 0 behaviors (MediatorBenchmarks)
    //   MediatorLiteSingleQuery → 1 behavior  (PipelineBenchmarks)
    //   MediatorLiteMultiQuery  → 3 behaviors (MultipleBehaviorsBenchmarks)

    public record MediatorLiteQuery(int Id) : MediatorLite.IRequest<MediatorLiteResult>;
    public record MediatorLiteResult(int Id, string Name);

    public class MediatorLiteHandler : MediatorLite.IRequestHandler<MediatorLiteQuery, MediatorLiteResult>
    {
        public ValueTask<MediatorLiteResult> HandleAsync(MediatorLiteQuery request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new MediatorLiteResult(request.Id, "Test"));
        }
    }

    public record MediatorLiteSingleQuery(int Id) : MediatorLite.IRequest<MediatorLiteResult>;

    public class MediatorLiteSingleQueryHandler : MediatorLite.IRequestHandler<MediatorLiteSingleQuery, MediatorLiteResult>
    {
        public ValueTask<MediatorLiteResult> HandleAsync(MediatorLiteSingleQuery request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new MediatorLiteResult(request.Id, "Test"));
        }
    }

    public class MediatorLiteSingleLoggingBehavior : MediatorLite.IPipelineBehavior<MediatorLiteSingleQuery, MediatorLiteResult>
    {
        public async ValueTask<MediatorLiteResult> HandleAsync(
            MediatorLiteSingleQuery request,
            MediatorLite.RequestHandlerDelegate<MediatorLiteResult> next,
            CancellationToken cancellationToken = default)
        {
            return await next();
        }
    }

    public record MediatorLiteMultiQuery(int Id) : MediatorLite.IRequest<MediatorLiteResult>;

    public class MediatorLiteMultiQueryHandler : MediatorLite.IRequestHandler<MediatorLiteMultiQuery, MediatorLiteResult>
    {
        public ValueTask<MediatorLiteResult> HandleAsync(MediatorLiteMultiQuery request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new MediatorLiteResult(request.Id, "Test"));
        }
    }

    public class MediatorLiteLoggingBehavior : MediatorLite.IPipelineBehavior<MediatorLiteMultiQuery, MediatorLiteResult>
    {
        public async ValueTask<MediatorLiteResult> HandleAsync(
            MediatorLiteMultiQuery request,
            MediatorLite.RequestHandlerDelegate<MediatorLiteResult> next,
            CancellationToken cancellationToken = default)
        {
            return await next();
        }
    }

    public class MediatorLiteValidationBehavior : MediatorLite.IPipelineBehavior<MediatorLiteMultiQuery, MediatorLiteResult>
    {
        public async ValueTask<MediatorLiteResult> HandleAsync(
            MediatorLiteMultiQuery request,
            MediatorLite.RequestHandlerDelegate<MediatorLiteResult> next,
            CancellationToken cancellationToken = default)
        {
            // Simulated validation - no actual work but adds to pipeline depth
            return await next();
        }
    }

    public class MediatorLiteMetricsBehavior : MediatorLite.IPipelineBehavior<MediatorLiteMultiQuery, MediatorLiteResult>
    {
        public async ValueTask<MediatorLiteResult> HandleAsync(
            MediatorLiteMultiQuery request,
            MediatorLite.RequestHandlerDelegate<MediatorLiteResult> next,
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
        mediatorLiteServices.AddMediatorLite();
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
        mediatorLiteServices.AddMediatorLite();
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
        return await _mediatorLite.SendAsync(new MediatorBenchmarks.MediatorLiteSingleQuery(1));
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
        mediatorLiteServices.AddMediatorLite();
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
        return await _mediatorLite.SendAsync(new MediatorBenchmarks.MediatorLiteMultiQuery(1));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        (_mediatorLiteProvider as IDisposable)?.Dispose();
        (_mediatrProvider as IDisposable)?.Dispose();
    }
}

// Benchmark with REAL FluentValidation in the pipeline.
// MediatorLite wires FluentValidationBehavior via the source generator (no assembly scan);
// MediatR uses the idiomatic hand-written validation behavior + AddValidatorsFromAssembly scan.
// Both run the same valid request through equivalent FluentValidation rules (happy path).
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ValidationBenchmarks
{
    private IServiceProvider _mediatorLiteProvider = null!;
    private IServiceProvider _mediatrProvider = null!;
    private MediatorLite.IMediator _mediatorLite = null!;
    private MediatR.IMediator _mediatr = null!;

    #region MediatorLite validated types

    public record MediatorLiteValidatedQuery(int Id, string Name)
        : MediatorLite.IRequest<MediatorBenchmarks.MediatorLiteResult>;

    public class MediatorLiteValidatedQueryHandler
        : MediatorLite.IRequestHandler<MediatorLiteValidatedQuery, MediatorBenchmarks.MediatorLiteResult>
    {
        public ValueTask<MediatorBenchmarks.MediatorLiteResult> HandleAsync(
            MediatorLiteValidatedQuery request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new MediatorBenchmarks.MediatorLiteResult(request.Id, request.Name));
        }
    }

    public sealed class MediatorLiteValidatedQueryValidator
        : FluentValidation.AbstractValidator<MediatorLiteValidatedQuery>
    {
        public MediatorLiteValidatedQueryValidator()
        {
            RuleFor(x => x.Id).GreaterThan(0);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        }
    }

    #endregion

    #region MediatR validated types

    public record MediatRValidatedQuery(int Id, string Name)
        : MediatR.IRequest<MediatorBenchmarks.MediatRResult>;

    public class MediatRValidatedQueryHandler
        : MediatR.IRequestHandler<MediatRValidatedQuery, MediatorBenchmarks.MediatRResult>
    {
        public Task<MediatorBenchmarks.MediatRResult> Handle(
            MediatRValidatedQuery request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new MediatorBenchmarks.MediatRResult(request.Id, request.Name));
        }
    }

    public sealed class MediatRValidatedQueryValidator
        : FluentValidation.AbstractValidator<MediatRValidatedQuery>
    {
        public MediatRValidatedQueryValidator()
        {
            RuleFor(x => x.Id).GreaterThan(0);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        }
    }

    // Canonical MediatR + FluentValidation pipeline behavior (resolves validators from DI,
    // throws FluentValidation.ValidationException on failure).
    public class MediatRFluentValidationBehavior<TRequest, TResponse>
        : MediatR.IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        private readonly IEnumerable<FluentValidation.IValidator<TRequest>> _validators;

        public MediatRFluentValidationBehavior(IEnumerable<FluentValidation.IValidator<TRequest>> validators)
        {
            _validators = validators;
        }

        public async Task<TResponse> Handle(
            TRequest request,
            MediatR.RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            foreach (var validator in _validators)
            {
                var result = await validator.ValidateAsync(request, cancellationToken);
                if (!result.IsValid)
                {
                    throw new FluentValidation.ValidationException(result.Errors);
                }
            }

            return await next();
        }
    }

    #endregion

    [GlobalSetup]
    public void Setup()
    {
        // MediatorLite: source generator wires the FluentValidation validator + FluentValidationBehavior.
        var mediatorLiteServices = new ServiceCollection();
        mediatorLiteServices.AddGeneratedHandlers();
        mediatorLiteServices.AddMediatorLite();
        mediatorLiteServices.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        mediatorLiteServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _mediatorLiteProvider = mediatorLiteServices.BuildServiceProvider();
        _mediatorLite = _mediatorLiteProvider.GetRequiredService<MediatorLite.IMediator>();

        // MediatR: hand-written validation behavior + idiomatic runtime assembly scan for validators.
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<MediatorBenchmarks>();
            cfg.AddOpenBehavior(typeof(MediatRFluentValidationBehavior<,>));
        });
        mediatrServices.AddValidatorsFromAssemblyContaining<MediatorBenchmarks>(ServiceLifetime.Transient);
        _mediatrProvider = mediatrServices.BuildServiceProvider();
        _mediatr = _mediatrProvider.GetRequiredService<MediatR.IMediator>();
    }

    [Benchmark(Baseline = true)]
    public async Task<MediatorBenchmarks.MediatRResult> MediatR_WithValidation()
    {
        return await _mediatr.Send(new MediatRValidatedQuery(1, "Test"));
    }

    [Benchmark]
    public async Task<MediatorBenchmarks.MediatorLiteResult> MediatorLite_WithValidation()
    {
        return await _mediatorLite.SendAsync(new MediatorLiteValidatedQuery(1, "Test"));
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
        mediatorLiteServices.AddMediatorLite();
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
