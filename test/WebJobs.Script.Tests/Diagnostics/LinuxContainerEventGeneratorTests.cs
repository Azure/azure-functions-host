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
    public class LinuxContainerEventGeneratorTests
    {
        private readonly LinuxContainerEventGenerator _generator;
        private readonly List<string> _events;
        private readonly string _containerName = "test-container";
        private readonly string _stampName = "test-stamp";
        private readonly string _tenantId = "test-tenant";
        private readonly string _testNodeAddress = "test-address";

        public LinuxContainerEventGeneratorTests()
        {
            _events = new List<string>();
            Action<string> writer = (s) =>
            {
                _events.Add(s);
            };

            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.ContainerName)).Returns(_containerName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteHomeStampName)).Returns(_stampName);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteStampDeploymentId)).Returns(_tenantId);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.LinuxNodeIpAddress)).Returns(_testNodeAddress);

            var standbyOptions = new TestOptionsMonitor<StandbyOptions>(new StandbyOptions { InStandbyMode = true });

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            var httpClient = new HttpClient(handlerMock.Object);

            _generator = new LinuxContainerEventGenerator(mockEnvironment.Object, writer);
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetLogEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseLogEvents(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName)
        {
            _generator.LogFunctionTraceEvent(level, subscriptionId, appName, functionName, eventName, source, details, summary, exceptionType, exceptionMessage, functionInvocationId, hostInstanceId, activityId, runtimeSiteName, slotName, DateTime.UtcNow);

            string evt = _events.Single();
            evt = JsonSerializeEvent(evt);

            Regex regex = new Regex(LinuxContainerEventGenerator.TraceEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(19, match.Groups.Count);

            DateTime dt;
            var groupMatches = match.Groups.Cast<Group>().Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal((int)LinuxEventGenerator.ToEventLevel(level), int.Parse(p)),
                p => Assert.Equal(subscriptionId, p),
                p => Assert.Equal(appName, p),
                p => Assert.Equal(functionName, p),
                p => Assert.Equal(eventName, p),
                p => Assert.Equal(source, p),
                p => Assert.Equal(details, UnNormalize(JsonUnescape(p))),
                p => Assert.Equal(summary, UnNormalize(JsonUnescape(p))),
                p => Assert.Equal(ScriptHost.Version, p),
                p => Assert.True(DateTime.TryParse(p, out dt)),
                p => Assert.Equal(exceptionType, p),
                p => Assert.Equal(exceptionMessage, UnNormalize(JsonUnescape(p))),
                p => Assert.Equal(functionInvocationId, p),
                p => Assert.Equal(hostInstanceId, p),
                p => Assert.Equal(activityId, p),
                p => Assert.Equal(_containerName.ToUpperInvariant(), p),
                p => Assert.Equal(_stampName, p),
                p => Assert.Equal(_tenantId, p));
        }

        private static string JsonUnescape(string value)
        {
            // Because the log data is being JSON serialized it ends up getting
            // escaped. This function reverses that escaping.
            return value.Replace("\\", string.Empty);
        }

        public static string UnNormalize(string normalized)
        {
            // We replace all double quotes to single before the writing the logs
            // to avoid our logging agents parsing break
            // TODO: we can remove this once platform is able to handle quotes in logs
            return normalized.Replace("'", "\"");
        }

        private static string JsonSerializeEvent(string evt)
        {
            // the logging pipeline currently wraps our raw log data with JSON
            // schema like the below
            JObject attribs = new JObject
            {
                { "ApplicationName", "TestApp" },
                { "CodePackageName", "TestCodePackage" }
            };
            JObject jo = new JObject
            {
                { "log", evt },
                { "stream", "stdout" },
                { "attrs", attribs },
                { "time", DateTime.UtcNow.ToString("o") }
            };

            return jo.ToString(Formatting.None);
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetMetricEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseMetricEvents(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, string data, string runtimeSiteName, string slotName)
        {
            _generator.LogFunctionMetricEvent(subscriptionId, appName, functionName, eventName, average, minimum, maximum, count, DateTime.Now, data, runtimeSiteName, slotName);

            string evt = _events.Single();
            evt = JsonSerializeEvent(evt);

            Regex regex = new Regex(LinuxContainerEventGenerator.MetricEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(15, match.Groups.Count);

            DateTime dt;
            var groupMatches = match.Groups.Cast<Group>().Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal(subscriptionId, p),
                p => Assert.Equal(appName, p),
                p => Assert.Equal(functionName, p),
                p => Assert.Equal(eventName, p),
                p => Assert.Equal(average, long.Parse(p)),
                p => Assert.Equal(minimum, long.Parse(p)),
                p => Assert.Equal(maximum, long.Parse(p)),
                p => Assert.Equal(count, long.Parse(p)),
                p => Assert.Equal(ScriptHost.Version, p),
                p => Assert.True(DateTime.TryParse(p, out dt)),
                p => Assert.Equal(data, JsonUnescape(p)),
                p => Assert.Equal(_containerName.ToUpperInvariant(), p),
                p => Assert.Equal(_stampName, p),
                p => Assert.Equal(_tenantId, p));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetDetailsEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseDetailsEvents(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            _generator.LogFunctionDetailsEvent(siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);

            string evt = _events.Single();
            evt = JsonSerializeEvent(evt);

            Regex regex = new Regex(LinuxContainerEventGenerator.DetailsEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(7, match.Groups.Count);

            var groupMatches = match.Groups.Cast<Group>().Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal(siteName, p),
                p => Assert.Equal(functionName, p),
                p => Assert.Equal(inputBindings, UnNormalize(JsonUnescape(p))),
                p => Assert.Equal(outputBindings, UnNormalize(JsonUnescape(p))),
                p => Assert.Equal(scriptType, p),
                p => Assert.Equal(isDisabled ? "1" : "0", p));
        }

        [Theory]
        [InlineData(LogLevel.Trace, EventLevel.Verbose)]
        [InlineData(LogLevel.Debug, EventLevel.Verbose)]
        [InlineData(LogLevel.Information, EventLevel.Informational)]
        [InlineData(LogLevel.Warning, EventLevel.Warning)]
        [InlineData(LogLevel.Error, EventLevel.Error)]
        [InlineData(LogLevel.Critical, EventLevel.Critical)]
        [InlineData(LogLevel.None, EventLevel.LogAlways)]
        public void ToEventLevel_ReturnsExpectedValue(LogLevel logLevel, EventLevel eventLevel)
        {
            Assert.Equal(eventLevel, LinuxEventGenerator.ToEventLevel(logLevel));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetAzureMonitorEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseAzureMonitoringEvents(LogLevel level, string resourceId, string operationName, string category, string regionName, string properties)
        {
            _generator.LogAzureMonitorDiagnosticLogEvent(level, resourceId, operationName, category, regionName, properties);

            string evt = _events.Single();

            Regex regex = new Regex(LinuxContainerEventGenerator.AzureMonitorEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(10, match.Groups.Count);

            var groupMatches = match.Groups.Cast<Group>().Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal((int)LinuxEventGenerator.ToEventLevel(level), int.Parse(p)),
                p => Assert.Equal(resourceId, p),
                p => Assert.Equal(operationName, p),
                p => Assert.Equal(category, p),
                p => Assert.Equal(regionName, p),
                p => Assert.Equal(properties, UnNormalize(p)),
                p => Assert.Equal(_containerName.ToUpperInvariant(), p),
                p => Assert.Equal(_tenantId, p),
                p => Assert.True(DateTime.TryParse(p, out DateTime dt)));
        }
    }
}
