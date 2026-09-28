# Benchmarks

Welcome to the benchmarks! This folder is for code benchmarking (e.g. components rather than end-to-do testing).
The intent is to benchmark areas we think are interesting and measure improvements as well as ensuring we don't unintentionally regress over time.

There's a lot of things that would be nice to have in benchmark form as we evaluate improving various parts of pipeline performance like async/await elimination,
`Task` vs. `ValueTask`, allocations, general algorithmic improvements, etc. This is where those assessments live.

To run benchmarks (from solution root - otherwise shorten the project path!):

```ps1
dotnet run -c Release -f net10.0 --project .\perf\WebJobs.Script.Benchmarks\ -- --inProcess
```

This will present a prompt with all benchmarks discovered - something like this:

```text
Available Benchmarks:
  #0 AuthUtilityBenchmarks
  #1 CSharpCompilationBenchmarks
  #2 ScriptLoggingBuilderExtensionsBenchmarks

You should select the target benchmark(s). Please, print a number of a benchmark (e.g. `0`) or a contained benchmark caption (e.g. `AuthUtilityBenchmarks`).
If you want to select few, please separate them with space ` ` (e.g. `1 2 3`).
You can also provide the class name in console arguments by using --filter. (e.g. `--filter *AuthUtilityBenchmarks*`).
```

Or, you can directly run a set of benchmarks from the command line as noted above:

```ps1
dotnet run -c Release -f net10.0 --project .\perf\WebJobs.Script.Benchmarks\ -- --inProcess --filter '*Grpc*'
```

Use the in-process toolchain with the repository's centralized build output layout; the current
BenchmarkDotNet version otherwise looks for generated executables in the SDK's default output location.

## HTTP request logging

`HttpRequestLoggingBenchmarks` measures completion-message formatting and structured-property enumeration
with one or three providers, including routes requiring JSON escaping.
`SystemTraceMiddlewareBenchmarks` measures tracing with zero, one or three authenticated identities,
with information logging enabled or disabled.

```ps1
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release -f net10.0 --project .\perf\WebJobs.Script.Benchmarks\ -- --inProcess --filter '*HttpRequestLoggingBenchmarks*' '*SystemTraceMiddlewareBenchmarks*' --join
```

Disabling tiered compilation matches the WebHost runtime setting. These are local component
microbenchmarks, not network latency, function execution, or production throughput measurements.
