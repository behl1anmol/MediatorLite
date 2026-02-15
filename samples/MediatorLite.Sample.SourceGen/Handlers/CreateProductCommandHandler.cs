using MediatorLite.Sample.SourceGen.Requests;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.SourceGen.Handlers;

public sealed class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, int>
{
    private readonly ILogger<CreateProductCommandHandler> _logger;
    private static int _nextProductId = 100;

    public CreateProductCommandHandler(ILogger<CreateProductCommandHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<int> HandleAsync(CreateProductCommand request, CancellationToken cancellationToken = default)
    {
        var productId = Interlocked.Increment(ref _nextProductId);

        _logger.LogInformation(
            "✅ Created product: {Name} (ID: {ProductId}, Price: {Price:C}, Stock: {Stock})",
            request.Name,
            productId,
            request.Price,
            request.InitialStock);

        return ValueTask.FromResult(productId);
    }
}
