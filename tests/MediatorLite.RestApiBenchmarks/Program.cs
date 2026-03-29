using BenchmarkDotNet.Running;
using MediatorLite.RestApiBenchmarks.Benchmarking;

BenchmarkSwitcher.FromTypes(
[
	typeof(RestApiReadWriteBenchmarks),
	typeof(RestApiConcurrencyBenchmarks)
]).Run(args);
