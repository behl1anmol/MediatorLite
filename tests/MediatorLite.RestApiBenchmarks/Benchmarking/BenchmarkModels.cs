namespace MediatorLite.RestApiBenchmarks.Benchmarking;

public enum MediatorImplementation
{
    MediatorLite = 0,
    MediatR = 1
}

public enum BenchmarkTransport
{
    InProcessTestServer = 0,
    LocalhostKestrel = 1
}
