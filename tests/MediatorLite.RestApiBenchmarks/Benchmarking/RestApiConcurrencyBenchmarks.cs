using System.Net;
using BenchmarkDotNet.Attributes;
using MediatorLite.RestApiBenchmarks.Data;
using MediatorLite.RestApiBenchmarks.Hosting;

namespace MediatorLite.RestApiBenchmarks.Benchmarking;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class RestApiConcurrencyBenchmarks
{
    private ApiBenchmarkHost _host = null!;
    private HttpClient _client = null!;

    [Params(MediatorImplementation.MediatR, MediatorImplementation.MediatorLite)]
    public MediatorImplementation Mediator { get; set; }

    [Params(BenchmarkTransport.InProcessTestServer, BenchmarkTransport.LocalhostKestrel)]
    public BenchmarkTransport Transport { get; set; }

    [Params(DatasetProfile.Medium)]
    public DatasetProfile Dataset { get; set; }

    [Params(1, 8, 32)]
    public int Concurrency { get; set; }

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

    [Benchmark]
    public async Task<int> Concurrent_Read_OrderDetails()
    {
        var tasks = new Task<HttpStatusCode>[Concurrency];
        for (var i = 0; i < Concurrency; i++)
        {
            var orderId = 100 + (i % 200);
            tasks[i] = SendStatusCodeAsync($"/api/orders/{orderId}");
        }

        var results = await Task.WhenAll(tasks);
        return results.Count(status => status == HttpStatusCode.OK);
    }

    [Benchmark]
    public async Task<int> Concurrent_Read_SearchOrders()
    {
        var tasks = new Task<HttpStatusCode>[Concurrency];
        for (var i = 0; i < Concurrency; i++)
        {
            var customerId = 20 + (i % 100);
            tasks[i] = SendStatusCodeAsync($"/api/orders/search?customerId={customerId}&page=1&pageSize=20");
        }

        var results = await Task.WhenAll(tasks);
        return results.Count(status => status == HttpStatusCode.OK);
    }

    private async Task<HttpStatusCode> SendStatusCodeAsync(string url)
    {
        using var response = await _client.GetAsync(url);
        return response.StatusCode;
    }
}
