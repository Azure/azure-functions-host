// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class KubernetesEventGeneratorTests
    {
        private readonly KubernetesEventGenerator _generator;
        private readonly List<string> _events;
        private readonly string _podName = "test-app-1234";

        public KubernetesEventGeneratorTests()
        {
            _events = new List<string>();
            Action<string> writer = (s) =>
            {
                _events.Add(s);
            };

            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.PodName)).Returns(_podName);
            _generator = new KubernetesEventGenerator(mockEnvironment.Object, writer);
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetLogEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseLogEvents(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName)
        {
            _generator.LogFunctionTraceEvent(level, subscriptionId, appName, functionName, eventName, source, details, summary, exceptionType, exceptionMessage, functionInvocationId, hostInstanceId, activityId, runtimeSiteName, slotName, DateTime.UtcNow);

            string evt = _events.Single();
            var jObject = JObject.Parse(evt);

            Assert.Equal(18, jObject.Properties().Count());

            DateTime dt;
            Assert.Collection(jObject.Properties(),
                p => Assert.Equal((int)LinuxEventGenerator.ToEventLevel(level), int.Parse(p.Value.ToString())),
                p => Assert.Equal(subscriptionId, p.Value),
                p => Assert.Equal(appName, p.Value),
                p => Assert.Equal(functionName, p.Value),
                p => Assert.Equal(eventName, p.Value),
                p => Assert.Equal(source, p.Value),
                p => Assert.Equal(LinuxEventGenerator.NormalizeString(details), p.Value.ToString()),
                p => Assert.Equal(LinuxEventGenerator.NormalizeString(summary), p.Value.ToString()),
                p => Assert.Equal(ScriptHost.Version, p.Value),
                p => Assert.True(DateTime.TryParse(p.Value.ToString(), out dt)),
                p => Assert.Equal(exceptionType, p.Value.ToString()),
                p => Assert.Equal(LinuxEventGenerator.NormalizeString(exceptionMessage), p.Value.ToString()),
                p => Assert.Equal(functionInvocationId, p.Value.ToString()),
                p => Assert.Equal(hostInstanceId, p.Value.ToString()),
                p => Assert.Equal(activityId, p.Value),
                p => Assert.Equal(runtimeSiteName, p.Value),
                p => Assert.Equal(slotName, p.Value),
                p => Assert.Equal(_podName, p.Value));
        }

        public static string UnNormalize(string normalized)
        {
            // We replace all double quotes to single before the writing the logs
            // to avoid our logging agents parsing break
            // TODO: we can remove this once platform is able to handle quotes in logs
            return normalized.Replace("'", "\"");
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetMetricEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseMetricEvents(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, string data, string runtimeSiteName, string slotName)
        {
            _generator.LogFunctionMetricEvent(subscriptionId, appName, functionName, eventName, average, minimum, maximum, count, DateTime.Now, data, runtimeSiteName, slotName);

            string evt = _events.Single();
            var jObject = JObject.Parse(evt);

            Assert.Equal(13, jObject.Properties().Count());

            DateTime dt;
            Assert.Collection(jObject.Properties(),
                p => Assert.Equal(subscriptionId, p.Value),
                p => Assert.Equal(appName, p.Value),
                p => Assert.Equal(functionName, p.Value),
                p => Assert.Equal(eventName, p.Value),
                p => Assert.Equal(average, long.Parse(p.Value.ToString())),
                p => Assert.Equal(minimum, long.Parse(p.Value.ToString())),
                p => Assert.Equal(maximum, long.Parse(p.Value.ToString())),
                p => Assert.Equal(count, long.Parse(p.Value.ToString())),
                p => Assert.Equal(ScriptHost.Version, p.Value),
                p => Assert.True(DateTime.TryParse(p.Value.ToString(), out dt)),
                p => Assert.Equal(LinuxEventGenerator.NormalizeString(data), p.Value),
                p => Assert.Equal(runtimeSiteName, p.Value),
                p => Assert.Equal(slotName, p.Value),
                p => Assert.Equal(_podName, p.Value));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetAzureMonitorEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseAzureMonitoringEvents(LogLevel level, string resourceId, string operationName, string category, string regionName, string properties)
        {
            _generator.LogAzureMonitorDiagnosticLogEvent(level, resourceId, operationName, category, regionName, properties);

            string evt = _events.Single();

            string expectedRegex = $"(?<Level>[0-6]),(?<ResourceId>[^,]*),(?<OperationName>[^,]*),(?<Category>[^,]*),(?<RegionName>[^,]*),\"(?<Properties>[^,]*)\",(?<EventTimestamp>[^,]+),(?<PodName>[^,]*)";
            Regex regex = new Regex(expectedRegex);

            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(9, match.Groups.Count);

            var groupMatches = match.Groups.Cast<Group>().Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal((int)LinuxEventGenerator.ToEventLevel(level), int.Parse(p)),
                p => Assert.Equal(resourceId, p),
                p => Assert.Equal(operationName, p),
                p => Assert.Equal(category, p),
                p => Assert.Equal(regionName, p),
                p => Assert.Equal(properties, UnNormalize(p)),
                p => Assert.True(DateTime.TryParse(p, out DateTime dt)),
                p => Assert.Equal(_podName, p));
        }
    }
}
