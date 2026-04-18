using MediatorLite.RestApiBenchmarks.Application.Common;
using MediatorLite.RestApiBenchmarks.Application.Contracts;
using MediatorLite.RestApiBenchmarks.Application.MediatR;
using MediatorLite.RestApiBenchmarks.Application.MediatorLite;
using MediatorLite.RestApiBenchmarks.Benchmarking;
using MediatorLite.RestApiBenchmarks.Data;
using MediatorLite.Generated;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ML = global::MediatorLite;
using MR = global::MediatR;

namespace MediatorLite.RestApiBenchmarks.Hosting;

public sealed class ApiBenchmarkHost : IAsyncDisposable
{
    public ApiBenchmarkHost(WebApplication app, HttpClient client, string databasePath)
    {
        App = app;
        Client = client;
        DatabasePath = databasePath;
    }

    public WebApplication App { get; }
    public HttpClient Client { get; }
    public string DatabasePath { get; }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();

        if (File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
        }
    }
}

public static class ApiBenchmarkHostFactory
{
    public static async Task<ApiBenchmarkHost> CreateAsync(
        MediatorImplementation mediatorImplementation,
        BenchmarkTransport transport,
        DatasetProfile datasetProfile,
        CancellationToken cancellationToken)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mediatorlite-rest-bench-{Guid.NewGuid():N}.db");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ApplicationName = typeof(ApiBenchmarkHostFactory).Assembly.FullName
        });

        builder.Logging.ClearProviders();

        if (transport == BenchmarkTransport.InProcessTestServer)
        {
            builder.WebHost.UseTestServer();
        }
        else
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        }

        builder.Services.AddRouting();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        builder.Services.AddScoped<OrderApplicationService>();
        builder.Services.AddScoped<IAppValidator<CreateOrderCommand>, CreateOrderCommandValidator>();

        if (mediatorImplementation == MediatorImplementation.MediatorLite)
        {
            builder.Services.AddGeneratedHandlers();
            builder.Services.AddMediatorLite(options =>
            {
                options.EnableBuiltInLogging = false;
                options.EnableTracing = false;
            });
        }
        else
        {
            builder.Services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(typeof(ApiBenchmarkHostFactory).Assembly);
                cfg.AddOpenBehavior(typeof(MediatRValidationBehavior<,>));
                cfg.AddOpenBehavior(typeof(MediatRLoggingBehavior<,>));
                cfg.AddOpenBehavior(typeof(MediatRMetricsBehavior<,>));
            });
        }

        var app = builder.Build();
        MapEndpoints(app);

        await app.StartAsync(cancellationToken);

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedData.EnsureCreatedAndSeedAsync(dbContext, datasetProfile, cancellationToken);
        }

        await BenchmarkParityGuard.ValidateAsync(app.Services, mediatorImplementation, datasetProfile, cancellationToken);

        var client = transport == BenchmarkTransport.InProcessTestServer
            ? app.GetTestClient()
            : new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

        return new ApiBenchmarkHost(app, client, databasePath);
    }

    private static void MapEndpoints(WebApplication app)
    {
        app.MapPost("/api/orders", async (CreateOrderCommand command, HttpContext context, CancellationToken ct) =>
        {
            try
            {
                var result = await SendAsync<CreateOrderResult>(context.RequestServices, command, ct);
                await PublishAsync(context.RequestServices, new OrderCreatedNotification(result.OrderId, command.CustomerId, result.TotalAmount), ct);
                return Results.Ok(result);
            }
            catch (AppValidationException ex)
            {
                return Results.BadRequest(new { Errors = ex.Errors });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        app.MapGet("/api/orders/{id:int}", async (int id, HttpContext context, CancellationToken ct) =>
        {
            try
            {
                var result = await SendAsync<OrderDetailsDto>(context.RequestServices, new GetOrderDetailsQuery(id), ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        });

        app.MapGet("/api/orders/search", async (int customerId, int page, int pageSize, HttpContext context, CancellationToken ct) =>
        {
            var result = await SendAsync<IReadOnlyList<OrderListItemDto>>(context.RequestServices, new SearchOrdersQuery(customerId, page, pageSize), ct);
            return Results.Ok(result);
        });

        app.MapGet("/api/reports/sales", async (int days, HttpContext context, CancellationToken ct) =>
        {
            var result = await SendAsync<SalesReportDto>(context.RequestServices, new GetSalesReportQuery(days), ct);
            return Results.Ok(result);
        });

        app.MapPost("/api/orders/{id:int}/cancel", async (int id, string reason, HttpContext context, CancellationToken ct) =>
        {
            try
            {
                var result = await SendAsync<CancelOrderResult>(context.RequestServices, new CancelOrderCommand(id, reason), ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        app.MapPost("/api/orders/{id:int}/status", async (int id, string expectedCurrentStatus, string newStatus, HttpContext context, CancellationToken ct) =>
        {
            try
            {
                var command = new UpdateOrderStatusCommand(id, expectedCurrentStatus, newStatus);
                var result = await SendAsync<UpdateOrderStatusResult>(context.RequestServices, command, ct);
                return Results.Ok(result);
            }
            catch (OrderConcurrencyException ex)
            {
                return Results.Conflict(new { Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        app.MapGet("/api/customers/{id:int}/summary", async (int id, int days, HttpContext context, CancellationToken ct) =>
        {
            var result = await SendAsync<CustomerOrderSummaryDto>(context.RequestServices, new GetCustomerOrderSummaryQuery(id, days), ct);
            return Results.Ok(result);
        });

        app.MapGet("/health", () => Results.Ok(new { Status = "ok" }));
    }

    private static async Task<TResponse> SendAsync<TResponse>(
        IServiceProvider serviceProvider,
        object request,
        CancellationToken cancellationToken)
    {
        if (serviceProvider.GetService<ML.IMediator>() is { } mediatorLite)
        {
            return await mediatorLite.SendAsync((ML.IRequest<TResponse>)request, cancellationToken);
        }

        var mediatR = serviceProvider.GetRequiredService<MR.IMediator>();
        return await mediatR.Send((MR.IRequest<TResponse>)request, cancellationToken);
    }

    private static async Task PublishAsync(IServiceProvider serviceProvider, object notification, CancellationToken cancellationToken)
    {
        if (serviceProvider.GetService<ML.IMediator>() is { } mediatorLite)
        {
            await mediatorLite.PublishAsync((ML.INotification)notification, cancellationToken);
            return;
        }

        var mediatR = serviceProvider.GetRequiredService<MR.IMediator>();
        await mediatR.Publish((MR.INotification)notification, cancellationToken);
    }
}
