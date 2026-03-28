namespace MediatorLite;

/// <summary>
/// Marker interface for requests that return a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The type of response returned by the request handler.</typeparam>
/// <remarks>
/// Implement this interface on your request/command/query types.
/// Each request type should have exactly one corresponding <see cref="IRequestHandler{TRequest,TResponse}"/>.
/// </remarks>
/// <example>
/// <code>
/// public record GetUserQuery(int Id) : IRequest&lt;User&gt;;
/// public record CreateUserCommand(string Name, string Email) : IRequest&lt;int&gt;;
/// </code>
/// </example>
public interface IRequest<out TResponse>;

/// <summary>
/// Marker interface for requests that don't return a meaningful response.
/// </summary>
/// <remarks>
/// This is a convenience interface that inherits from <see cref="IRequest{TResponse}"/>
/// with <see cref="Unit"/> as the response type. Use this for commands that don't need
/// to return a value.
/// </remarks>
/// <example>
/// <code>
/// public record DeleteUserCommand(int Id) : IRequest;
/// </code>
/// </example>
public interface IRequest : IRequest<Unit>;
