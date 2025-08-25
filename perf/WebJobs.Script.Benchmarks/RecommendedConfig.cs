// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using Perfolizer.Horology;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    /// <summary>
    /// Default config largely matching the dotnet/performance repo:
    /// https://github.com/dotnet/performance/blob/a453235176943a222f126762b4b5436f6687d62e/src/harness/BenchmarkDotNet.Extensions/RecommendedConfig.cs
    /// </summary>
    internal static class RecommendedConfig
    {
        public static IConfig Create(DirectoryInfo artifactsPath)
        {
            var job = Job.Default
                .WithWarmupCount(1) // 1 warmup is enough for our purpose
                .WithIterationTime(TimeInterval.FromMilliseconds(250)) // the default is 0.5s per iteration, which is slighlty too much for us
                .WithMinIterationCount(15)
                .WithMaxIterationCount(20); // we don't want to run more that 20 iterations

            return DefaultConfig.Instance
                .AddLogger(ConsoleLogger.Default) // log output to console
                .AddExporter(MarkdownExporter.GitHub) // export to GitHub markdown
                .AddColumnProvider(DefaultColumnProviders.Instance) // display default columns (method name, args etc)
                .AddJob(job.AsDefault()) // tell BDN that this are our default settings
                .WithArtifactsPath(artifactsPath.FullName)
                .AddDiagnoser(MemoryDiagnoser.Default) // MemoryDiagnoser is enabled by default
                .AddExporter(JsonExporter.Full) // make sure we export to Json
                .AddColumn(StatisticColumn.Median, StatisticColumn.Min, StatisticColumn.Max)
                .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(36)); // the default is 20 and trims too aggressively some benchmark results
        }
    }
}
