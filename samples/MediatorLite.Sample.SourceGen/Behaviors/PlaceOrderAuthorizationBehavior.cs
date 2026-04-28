using MediatorLite.Sample.SourceGen.Requests;
using Microsoft.Extensions.Logging;

namespace MediatorLite.Sample.SourceGen.Behaviors;

/// <summary>
/// Closed (non-generic) behavior that applies only to PlaceOrderCommand.
/// Demonstrates request-specific authorization logic.
/// </summary>
public sealed class PlaceOrderAuthorizationBehavior
    : IPipelineBehavior<PlaceOrderCommand, OrderResult>
{
    private readonly ILogger<PlaceOrderAuthorizationBehavior> _logger;

    public PlaceOrderAuthorizationBehavior(ILogger<PlaceOrderAuthorizationBehavior> logger)
    {
        _logger = logger;
    }

    public async ValueTask<OrderResult> HandleAsync(
        PlaceOrderCommand request,
        RequestHandlerDelegate<OrderResult> next,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "🔒 [Closed Behavior] Authorizing order placement for product {ProductId}",
            request.ProductId);

        // Simulate authorization check
        if (request.Quantity > 100)
        {
            _logger.LogWarning(
                "⚠️ Large order detected ({Quantity} units) - requires manager approval",
                request.Quantity);

            // In a real application, you might:
            // - Check user permissions
            // - Verify credit limits
            // - Send approval notifications
            // - Throw UnauthorizedException if needed
        }

        // Check for suspicious patterns (demo only)
        if (string.IsNullOrWhiteSpace(request.CustomerEmail) ||
            !request.CustomerEmail.Contains('@'))
        {
            _logger.LogError("❌ Invalid customer email: {Email}", MaskEmail(request.CustomerEmail));
            throw new InvalidOperationException(
                "Order authorization failed: invalid customer email");
        }

        _logger.LogDebug("✅ Order authorization passed");

        // Call the next behavior or handler
        return await next();
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return email;
        var parts = email.Split('@');
        if (parts.Length != 2) return "***";

        var username = parts[0];
        var domain = parts[1];

        if (username.Length <= 1) return $"*@{domain}";

        return $"{username[0]}***@{domain}";
    }
}
