# Benchmarks

Welcome to the benchmarks! This folder is for code benchmarking (e.g. components rather than end-to-do testing).
The intent is to benchmark areas we think are interesting and measure improvements as well as ensuring we don't unintentionally regress over time.

There's a lot of things that would be nice to have in benchmark form as we evaluate improving various parts of pipeline performance like async/await elimination,
`Task` vs. `ValueTask`, allocations, general algorithmic improvements, etc. This is where those assessments live.

To run benchmarks (from solution root - otherwise shorten the project path!):

```ps1
dotnet run -c Release -f net6.0 --project .\benchmarks\WebJobs.Script.Benchmarks\
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
dotnet run -c Release -f net6.0 --project .\benchmarks\WebJobs.Script.Benchmarks\ --filter *Grpc*
```
