using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Snail.MCP.Plans.Benchmarks.Sample).Assembly).Run(args);
