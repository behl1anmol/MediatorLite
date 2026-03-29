namespace MediatorLite;

/// <summary>
/// Delegate representing the next handler or behavior in the request pipeline.
/// </summary>
/// <typeparam name="TResponse">The type of response from the handler.</typeparam>
/// <returns>A <see cref="ValueTask{TResponse}"/> representing the response.</returns>
public delegate ValueTask<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Defines a pipeline behavior that wraps request handling with cross-cutting concerns.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled.</typeparam>
/// <typeparam name="TResponse">The type of response from the handler.</typeparam>
/// <remarks>
/// Pipeline behaviors are executed in order of registration, wrapping the actual handler.
/// Use behaviors for cross-cutting concerns like logging, validation, caching, etc.
/// The execution order can be controlled via the <see cref="BehaviorOrderAttribute"/>.
/// </remarks>
/// <example>
/// <code>
/// public class LoggingBehavior&lt;TRequest, TResponse&gt; : IPipelineBehavior&lt;TRequest, TResponse&gt;
///     where TRequest : IRequest&lt;TResponse&gt;
/// {
///     private readonly ILogger&lt;LoggingBehavior&lt;TRequest, TResponse&gt;&gt; _logger;
///     
///     public LoggingBehavior(ILogger&lt;LoggingBehavior&lt;TRequest, TResponse&gt;&gt; logger)
///     {
///         _logger = logger;
///     }
///     
///     public async ValueTask&lt;TResponse&gt; HandleAsync(
///         TRequest request,
///         RequestHandlerDelegate&lt;TResponse&gt; next,
///         CancellationToken cancellationToken = default)
///     {
///         _logger.LogInformation("Handling {RequestType}", typeof(TRequest).Name);
///         var response = await next();
///         _logger.LogInformation("Handled {RequestType}", typeof(TRequest).Name);
///         return response;
///     }
/// }
/// </code>
/// </example>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the request by optionally performing work before/after invoking the next handler.
    /// </summary>
    /// <param name="request">The request being handled.</param>
    /// <param name="next">The delegate to invoke the next behavior or the actual handler.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="ValueTask{TResponse}"/> representing the response.</returns>
    ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}
