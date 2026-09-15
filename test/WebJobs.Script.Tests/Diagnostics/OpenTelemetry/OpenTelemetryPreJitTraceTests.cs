// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Diagnostics.JitTrace;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.OpenTelemetry
{
    public class OpenTelemetryPreJitTraceTests
    {
        [Fact]
        public void OpenTelemetryPreJitTrace_PreparesAllEntries()
        {
            string path = GetPreJitFilePath(WarmUpConstants.OpenTelemetryJitTraceFileName);
            string trace = File.ReadAllText(path);
            var failures = new List<string>();

            try
            {
                JitTraceRuntime.LogFailure += failures.Add;
                JitTraceRuntime.Prepare(trace, out int successfulPrepares, out int failedPrepares);

                Assert.True(successfulPrepares > 0);
                Assert.True(failedPrepares == 0, string.Join(Environment.NewLine, failures));
            }
            finally
            {
                JitTraceRuntime.LogFailure -= failures.Add;
            }
        }

        [Theory]
        [InlineData("Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry.OpenTelemetryConfigurationExtensions.ConfigureOpenTelemetry")]
        [InlineData("Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry.OpenTelemetryConfigurationExtensions.ConfigureExporters")]
        [InlineData("Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry.OpenTelemetryConfigurationExtensions.ConfigureTracing")]
        [InlineData("Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry.OpenTelemetryConfigurationExtensions.ConfigureMetrics")]
        [InlineData("Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry.OpenTelemetryConfigurationExtensions.ConfigureLogging")]
        [InlineData("OpenTelemetry.OpenTelemetryBuilderOtlpExporterExtensions.UseOtlpExporter")]
        [InlineData("Azure.Monitor.OpenTelemetry.Exporter.OpenTelemetryBuilderExtensions.UseAzureMonitorExporter")]
        [InlineData("Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorTraceExporter")]
        [InlineData("Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorMetricExporter")]
        [InlineData("Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorLogExporter")]
        public void OpenTelemetryPreJitTrace_IncludesRequiredIntegrationPath(string entry)
        {
            string path = GetPreJitFilePath(WarmUpConstants.OpenTelemetryJitTraceFileName);
            string[] traceEntries = File.ReadAllLines(path);

            Assert.Contains(traceEntries, line => line.Contains(entry, StringComparison.Ordinal));
        }

        [Fact]
        public void OpenTelemetryPreJitTrace_IsCopiedWithWebHostPreJitFiles()
        {
            string preJitPath = Path.GetDirectoryName(new Uri(typeof(HostWarmupMiddleware).Assembly.Location).LocalPath);

            Assert.True(File.Exists(Path.Combine(preJitPath, WarmUpConstants.PreJitFolderName, WarmUpConstants.OpenTelemetryJitTraceFileName)));
        }

        private static string GetPreJitFilePath(string fileName)
        {
            string assemblyPath = Path.GetDirectoryName(new Uri(typeof(HostWarmupMiddleware).Assembly.Location).LocalPath);

            return Path.Combine(assemblyPath, WarmUpConstants.PreJitFolderName, fileName);
        }
    }
}
