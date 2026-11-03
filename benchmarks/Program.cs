using BenchmarkDotNet.Running;
using Snail.MCP.BuildBridge.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(AnswerBenchmarks).Assembly).Run(args);
