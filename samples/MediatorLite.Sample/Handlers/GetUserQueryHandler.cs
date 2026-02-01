using MediatorLite;
using MediatorLite.Sample.Requests;

namespace MediatorLite.Sample.Handlers;

public class GetUserQueryHandler : IRequestHandler<GetUserQuery, User>
{
    public ValueTask<User> HandleAsync(GetUserQuery request, CancellationToken cancellationToken = default)
    {
        // Simulate database lookup
        var user = new User(request.Id, "John Doe", "john.doe@example.com");
        return ValueTask.FromResult(user);
    }
}
