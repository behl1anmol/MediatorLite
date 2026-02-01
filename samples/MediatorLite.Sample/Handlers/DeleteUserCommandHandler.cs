using MediatorLite;
using MediatorLite.Sample.Requests;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.Handlers;

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(ILogger<DeleteUserCommandHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask HandleAsync(DeleteUserCommand request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting user with ID: {UserId}", request.Id);
        // Simulate delete operation
        return ValueTask.CompletedTask;
    }
}
