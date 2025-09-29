// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public sealed class KubernetesEventGeneratorTests : IDisposable
    {
        private readonly StringWriter _writer = new();

        public void Dispose()
        {
            _writer.Dispose();
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetLogEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void LoggingDisabled(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName)
        {
            var options = CreateLoggingOptions(consoleDisabled: true);
            var generator = new KubernetesEventGenerator(options);

            generator.LogFunctionTraceEvent(level, subscriptionId, appName, functionName, eventName, source, details, summary, exceptionType, exceptionMessage, functionInvocationId, hostInstanceId, activityId, runtimeSiteName, slotName, DateTime.UtcNow);

            string output = _writer.ToString().Trim();

            Assert.True(string.IsNullOrEmpty(output));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetLogEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseLogEvents(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName)
        {
            var options = CreateLoggingOptions(consoleDisabled: false);
            var generator = new KubernetesEventGenerator(options);

            generator.LogFunctionTraceEvent(level, subscriptionId, appName, functionName, eventName, source, details, summary, exceptionType, exceptionMessage, functionInvocationId, hostInstanceId, activityId, runtimeSiteName, slotName, DateTime.UtcNow);

            string output = _writer.ToString().Trim();
            var jObject = JObject.Parse(output);

            Assert.Equal(18, jObject.Properties().Count());
            Assert.Collection(jObject.Properties(),
                p => Assert.Equal(ScriptConstants.LinuxLogEventStreamName, p.Value),
                p => Assert.Equal((int)LinuxEventGenerator.ToEventLevel(level), int.Parse(p.Value.ToString())),
                p => Assert.Equal(subscriptionId, p.Value),
                p => Assert.Equal(appName, p.Value),
                p => Assert.Equal(functionName, p.Value),
                p => Assert.Equal(eventName, p.Value),
                p => Assert.Equal(source, p.Value),
                p => Assert.Equal(LinuxEventGenerator.NormalizeString(details, addEnclosingQuotes: false), p.Value.ToString()),
                p => Assert.Equal(LinuxEventGenerator.NormalizeString(summary, addEnclosingQuotes: false), p.Value.ToString()),
                p => Assert.Equal(ScriptHost.Version, p.Value),
                p => Assert.True(DateTime.TryParse(p.Value.ToString(), out _)),
                p => Assert.Equal(exceptionType, p.Value.ToString()),
                p => Assert.Equal(LinuxEventGenerator.NormalizeString(exceptionMessage, addEnclosingQuotes: false), p.Value.ToString()),
                p => Assert.Equal(functionInvocationId, p.Value.ToString()),
                p => Assert.Equal(hostInstanceId, p.Value.ToString()),
                p => Assert.Equal(activityId, p.Value),
                p => Assert.Equal(runtimeSiteName, p.Value),
                p => Assert.Equal(slotName, p.Value));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetMetricEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseMetricEvents(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, string data, string runtimeSiteName, string slotName)
        {
            var options = CreateLoggingOptions(consoleDisabled: false);
            var generator = new KubernetesEventGenerator(options);

            generator.LogFunctionMetricEvent(subscriptionId, appName, functionName, eventName, average, minimum, maximum, count, DateTime.Now, data, runtimeSiteName, slotName);

            string output = _writer.ToString().Trim();
            var jObject = JObject.Parse(output);

            Assert.Equal(14, jObject.Properties().Count());

            Assert.Collection(jObject.Properties(),
                p => Assert.Equal(ScriptConstants.LinuxMetricEventStreamName, p.Value),
                p => Assert.Equal(subscriptionId, p.Value),
                p => Assert.Equal(appName, p.Value),
                p => Assert.Equal(functionName, p.Value),
                p => Assert.Equal(eventName, p.Value),
                p => Assert.Equal(average, long.Parse(p.Value.ToString())),
                p => Assert.Equal(minimum, long.Parse(p.Value.ToString())),
                p => Assert.Equal(maximum, long.Parse(p.Value.ToString())),
                p => Assert.Equal(count, long.Parse(p.Value.ToString())),
                p => Assert.Equal(ScriptHost.Version, p.Value),
                p => Assert.True(DateTime.TryParse(p.Value.ToString(), out _)),
                p => Assert.Equal(LinuxEventGenerator.NormalizeString(data), p.Value),
                p => Assert.Equal(runtimeSiteName, p.Value),
                p => Assert.Equal(slotName, p.Value));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetAzureMonitorEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseAzureMonitoringEvents(LogLevel level, string resourceId, string operationName, string category, string regionName, string properties)
        {
            var options = CreateLoggingOptions(consoleDisabled: false);
            var generator = new KubernetesEventGenerator(options);

            generator.LogAzureMonitorDiagnosticLogEvent(level, resourceId, operationName, category, regionName, properties);

            string output = _writer.ToString().Trim();
            var jObject = JObject.Parse(output);

            Assert.Equal(7, jObject.Properties().Count());

            Assert.Collection(jObject.Properties(),
                p => Assert.Equal(ScriptConstants.LinuxAzureMonitorEventStreamName, p.Value),
                p => Assert.Equal((int)LinuxEventGenerator.ToEventLevel(level), int.Parse(p.Value.ToString())),
                p => Assert.Equal(resourceId, p.Value),
                p => Assert.Equal(operationName, p.Value),
                p => Assert.Equal(category, p.Value),
                p => Assert.Equal(regionName, p.Value),
                p => Assert.Equal(LinuxEventGenerator.NormalizeString(properties), p.Value));
        }

        private IOptions<ConsoleLoggingOptions> CreateLoggingOptions(bool? consoleDisabled = null)
        {
            ConsoleLoggingOptions options = new()
            {
                Writer = _writer
            };

            if (consoleDisabled.HasValue)
            {
                options.LoggingDisabled = consoleDisabled.Value;
            }

            return Options.Create(options);
        }
    }
}
