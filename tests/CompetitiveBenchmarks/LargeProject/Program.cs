using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(CompetitiveBenchmarks.LargeProject.Setup).Assembly).Run(args);
