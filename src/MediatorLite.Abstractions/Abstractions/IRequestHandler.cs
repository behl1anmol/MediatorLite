namespace MediatorLite;

/// <summary>
/// Defines a handler for a request of type <typeparamref name="TRequest"/>
/// that returns a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled.</typeparam>
/// <typeparam name="TResponse">The type of response from the handler.</typeparam>
/// <remarks>
/// Each request type should have exactly one handler registered.
/// Handlers are resolved from the dependency injection container.
/// </remarks>
/// <example>
/// <code>
/// public class GetUserQueryHandler : IRequestHandler&lt;GetUserQuery, User&gt;
/// {
///     private readonly IUserRepository _repository;
///     
///     public GetUserQueryHandler(IUserRepository repository)
///     {
///         _repository = repository;
///     }
///     
///     public async ValueTask&lt;User&gt; HandleAsync(GetUserQuery request, CancellationToken cancellationToken = default)
///     {
///         return await _repository.GetByIdAsync(request.Id, cancellationToken);
///     }
/// }
/// </code>
/// </example>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles a request asynchronously.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ValueTask{TResponse}"/> representing the asynchronous operation with the response.</returns>
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a handler for a request of type <typeparamref name="TRequest"/> that doesn't return a value.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled.</typeparam>
/// <remarks>
/// This is a convenience interface for handlers that don't need to return a value.
/// Implement this instead of <see cref="IRequestHandler{TRequest,TResponse}"/> with <see cref="Unit"/>.
/// </remarks>
/// <example>
/// <code>
/// public class DeleteUserCommandHandler : IRequestHandler&lt;DeleteUserCommand&gt;
/// {
///     public async ValueTask HandleAsync(DeleteUserCommand request, CancellationToken cancellationToken = default)
///     {
///         await _repository.DeleteAsync(request.Id, cancellationToken);
///     }
/// }
/// </code>
/// </example>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{
    /// <summary>
    /// Handles a request asynchronously without returning a value.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    new ValueTask HandleAsync(TRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicit implementation that wraps the void HandleAsync to return Unit.
    /// </summary>
    async ValueTask<Unit> IRequestHandler<TRequest, Unit>.HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        await HandleAsync(request, cancellationToken);
        return Unit.Value;
    }
}
