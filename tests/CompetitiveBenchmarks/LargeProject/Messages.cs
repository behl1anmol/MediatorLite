namespace CompetitiveBenchmarks.LargeProject;

/// <summary>Shared response DTO so all three libraries return an identical shape.</summary>
public sealed record Result(int Value);

// ─────────────────────────────── MediatorLite ───────────────────────────────
public sealed record MlQuery(int Id) : MediatorLite.IRequest<Result>;

public sealed class MlQueryHandler : MediatorLite.IRequestHandler<MlQuery, Result>
{
    public ValueTask<Result> HandleAsync(MlQuery request, CancellationToken cancellationToken = default)
        => new(new Result(request.Id));
}

public sealed record MlPipelineQuery(int Id) : MediatorLite.IRequest<Result>;

public sealed class MlPipelineQueryHandler : MediatorLite.IRequestHandler<MlPipelineQuery, Result>
{
    public ValueTask<Result> HandleAsync(MlPipelineQuery request, CancellationToken cancellationToken = default)
        => new(new Result(request.Id));
}

[MediatorLite.BehaviorOrder(1)]
public sealed class MlBehavior1 : MediatorLite.IPipelineBehavior<MlPipelineQuery, Result>
{
    public ValueTask<Result> HandleAsync(MlPipelineQuery request, MediatorLite.RequestHandlerDelegate<Result> next, CancellationToken cancellationToken = default) => next();
}

[MediatorLite.BehaviorOrder(2)]
public sealed class MlBehavior2 : MediatorLite.IPipelineBehavior<MlPipelineQuery, Result>
{
    public ValueTask<Result> HandleAsync(MlPipelineQuery request, MediatorLite.RequestHandlerDelegate<Result> next, CancellationToken cancellationToken = default) => next();
}

[MediatorLite.BehaviorOrder(3)]
public sealed class MlBehavior3 : MediatorLite.IPipelineBehavior<MlPipelineQuery, Result>
{
    public ValueTask<Result> HandleAsync(MlPipelineQuery request, MediatorLite.RequestHandlerDelegate<Result> next, CancellationToken cancellationToken = default) => next();
}

public sealed record MlNotification(int Id) : MediatorLite.INotification;

public sealed class MlNotificationHandler1 : MediatorLite.INotificationHandler<MlNotification>
{
    public ValueTask HandleAsync(MlNotification notification, CancellationToken cancellationToken = default) => default;
}

public sealed class MlNotificationHandler2 : MediatorLite.INotificationHandler<MlNotification>
{
    public ValueTask HandleAsync(MlNotification notification, CancellationToken cancellationToken = default) => default;
}

public sealed class MlNotificationHandler3 : MediatorLite.INotificationHandler<MlNotification>
{
    public ValueTask HandleAsync(MlNotification notification, CancellationToken cancellationToken = default) => default;
}

// ──────────────────────────── Mediator (martinothamar) ────────────────────────────
public sealed record MoQuery(int Id) : Mediator.IRequest<Result>;

public sealed class MoQueryHandler : Mediator.IRequestHandler<MoQuery, Result>
{
    public ValueTask<Result> Handle(MoQuery request, CancellationToken cancellationToken)
        => new(new Result(request.Id));
}

public sealed record MoPipelineQuery(int Id) : Mediator.IRequest<Result>;

public sealed class MoPipelineQueryHandler : Mediator.IRequestHandler<MoPipelineQuery, Result>
{
    public ValueTask<Result> Handle(MoPipelineQuery request, CancellationToken cancellationToken)
        => new(new Result(request.Id));
}

public sealed class MoBehavior1 : Mediator.IPipelineBehavior<MoPipelineQuery, Result>
{
    public ValueTask<Result> Handle(MoPipelineQuery message, Mediator.MessageHandlerDelegate<MoPipelineQuery, Result> next, CancellationToken cancellationToken) => next(message, cancellationToken);
}

public sealed class MoBehavior2 : Mediator.IPipelineBehavior<MoPipelineQuery, Result>
{
    public ValueTask<Result> Handle(MoPipelineQuery message, Mediator.MessageHandlerDelegate<MoPipelineQuery, Result> next, CancellationToken cancellationToken) => next(message, cancellationToken);
}

public sealed class MoBehavior3 : Mediator.IPipelineBehavior<MoPipelineQuery, Result>
{
    public ValueTask<Result> Handle(MoPipelineQuery message, Mediator.MessageHandlerDelegate<MoPipelineQuery, Result> next, CancellationToken cancellationToken) => next(message, cancellationToken);
}

public sealed record MoNotification(int Id) : Mediator.INotification;

public sealed class MoNotificationHandler1 : Mediator.INotificationHandler<MoNotification>
{
    public ValueTask Handle(MoNotification notification, CancellationToken cancellationToken) => default;
}

public sealed class MoNotificationHandler2 : Mediator.INotificationHandler<MoNotification>
{
    public ValueTask Handle(MoNotification notification, CancellationToken cancellationToken) => default;
}

public sealed class MoNotificationHandler3 : Mediator.INotificationHandler<MoNotification>
{
    public ValueTask Handle(MoNotification notification, CancellationToken cancellationToken) => default;
}

// ─────────────────────────────────── MediatR ───────────────────────────────────
public sealed record MrQuery(int Id) : MediatR.IRequest<Result>;

public sealed class MrQueryHandler : MediatR.IRequestHandler<MrQuery, Result>
{
    private static readonly Task<Result> Cached = Task.FromResult(new Result(0));
    public Task<Result> Handle(MrQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new Result(request.Id));
}

public sealed record MrPipelineQuery(int Id) : MediatR.IRequest<Result>;

public sealed class MrPipelineQueryHandler : MediatR.IRequestHandler<MrPipelineQuery, Result>
{
    public Task<Result> Handle(MrPipelineQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new Result(request.Id));
}

public sealed class MrBehavior1 : MediatR.IPipelineBehavior<MrPipelineQuery, Result>
{
    public Task<Result> Handle(MrPipelineQuery request, MediatR.RequestHandlerDelegate<Result> next, CancellationToken cancellationToken) => next(cancellationToken);
}

public sealed class MrBehavior2 : MediatR.IPipelineBehavior<MrPipelineQuery, Result>
{
    public Task<Result> Handle(MrPipelineQuery request, MediatR.RequestHandlerDelegate<Result> next, CancellationToken cancellationToken) => next(cancellationToken);
}

public sealed class MrBehavior3 : MediatR.IPipelineBehavior<MrPipelineQuery, Result>
{
    public Task<Result> Handle(MrPipelineQuery request, MediatR.RequestHandlerDelegate<Result> next, CancellationToken cancellationToken) => next(cancellationToken);
}

public sealed record MrNotification(int Id) : MediatR.INotification;

public sealed class MrNotificationHandler1 : MediatR.INotificationHandler<MrNotification>
{
    public Task Handle(MrNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class MrNotificationHandler2 : MediatR.INotificationHandler<MrNotification>
{
    public Task Handle(MrNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class MrNotificationHandler3 : MediatR.INotificationHandler<MrNotification>
{
    public Task Handle(MrNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
