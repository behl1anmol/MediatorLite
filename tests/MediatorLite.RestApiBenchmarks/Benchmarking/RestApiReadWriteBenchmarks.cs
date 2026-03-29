using System.Net;
using System.Net.Http.Json;
using BenchmarkDotNet.Attributes;
using MediatorLite.RestApiBenchmarks.Application.Contracts;
using MediatorLite.RestApiBenchmarks.Data;
using MediatorLite.RestApiBenchmarks.Hosting;

namespace MediatorLite.RestApiBenchmarks.Benchmarking;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class RestApiReadWriteBenchmarks
{
    private ApiBenchmarkHost _host = null!;
    private HttpClient _client = null!;

    [Params(MediatorImplementation.MediatR, MediatorImplementation.MediatorLite)]
    public MediatorImplementation Mediator { get; set; }

    [Params(BenchmarkTransport.InProcessTestServer, BenchmarkTransport.LocalhostKestrel)]
    public BenchmarkTransport Transport { get; set; }

    [Params(DatasetProfile.Medium)]
    public DatasetProfile Dataset { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _host = await ApiBenchmarkHostFactory.CreateAsync(Mediator, Transport, Dataset, CancellationToken.None);
        _client = _host.Client;
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _host.DisposeAsync();
    }

    [Benchmark(Baseline = true)]
    public async Task<HttpStatusCode> Read_OrderDetails()
    {
        using var response = await _client.GetAsync("/api/orders/100");
        return response.StatusCode;
    }

    [Benchmark]
    public async Task<HttpStatusCode> Read_SearchOrders()
    {
        using var response = await _client.GetAsync("/api/orders/search?customerId=30&page=1&pageSize=25");
        return response.StatusCode;
    }

    [Benchmark]
    public async Task<HttpStatusCode> Read_SalesReport()
    {
        using var response = await _client.GetAsync("/api/reports/sales?days=30");
        return response.StatusCode;
    }

    [Benchmark]
    public async Task<HttpStatusCode> Read_CustomerSummary()
    {
        using var response = await _client.GetAsync("/api/customers/30/summary?days=60");
        return response.StatusCode;
    }

    [Benchmark]
    public async Task<HttpStatusCode> Write_CreateOrder()
    {
        var request = new CreateOrderCommand(
            CustomerId: 10,
            Lines:
            [
                new CreateOrderLineInput(20, 1),
                new CreateOrderLineInput(21, 2),
                new CreateOrderLineInput(22, 1)
            ],
            CorrelationId: "bench-create-order-001");

        using var response = await _client.PostAsJsonAsync("/api/orders", request);
        return response.StatusCode;
    }

    [Benchmark]
    public async Task<HttpStatusCode> Write_CreateOrder_ValidationFailure()
    {
        var request = new CreateOrderCommand(
            CustomerId: 10,
            Lines:
            [
                new CreateOrderLineInput(20, 1),
                new CreateOrderLineInput(20, 2)
            ],
            CorrelationId: "bench-invalid-order-001");

        using var response = await _client.PostAsJsonAsync("/api/orders", request);
        return response.StatusCode;
    }

    [Benchmark]
    public async Task<HttpStatusCode> Write_CancelOrder()
    {
        var createRequest = new CreateOrderCommand(
            CustomerId: 12,
            Lines:
            [
                new CreateOrderLineInput(25, 1),
                new CreateOrderLineInput(26, 1)
            ],
            CorrelationId: $"bench-cancel-{Guid.NewGuid():N}");

        using var createResponse = await _client.PostAsJsonAsync("/api/orders", createRequest);
        if (createResponse.StatusCode != HttpStatusCode.OK)
        {
            return createResponse.StatusCode;
        }

        var created = await createResponse.Content.ReadFromJsonAsync<CreateOrderResult>();
        if (created is null)
        {
            return HttpStatusCode.InternalServerError;
        }

        using var cancelResponse = await _client.PostAsync($"/api/orders/{created.OrderId}/cancel?reason=customer-request", content: null);
        return cancelResponse.StatusCode;
    }

    [Benchmark]
    public async Task<HttpStatusCode> Write_UpdateOrderStatus_Conflict()
    {
        using var response = await _client.PostAsync("/api/orders/140/status?expectedCurrentStatus=Cancelled&newStatus=Completed", content: null);
        return response.StatusCode;
    }
}
