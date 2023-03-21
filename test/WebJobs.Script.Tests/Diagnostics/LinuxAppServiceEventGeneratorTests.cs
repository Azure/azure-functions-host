// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class LinuxAppServiceEventGeneratorTests
    {
        private const string _hostNameDefault = "SimpleApp";

        private readonly LinuxAppServiceEventGenerator _generator;
        private readonly List<string> _events;
        private IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;

        public LinuxAppServiceEventGeneratorTests()
        {
            _events = new List<string>();
            Action<string> writer = (s) =>
            {
                _events.Add(s);
            };

            var loggerFactoryMock = new MockLinuxAppServiceFileLoggerFactory();

            _functionsHostingConfigOptions = Options.Create(new FunctionsHostingConfigOptions());

            var environmentMock = new Mock<IEnvironment>();
            environmentMock.Setup(f => f.GetEnvironmentVariable(It.Is<string>(v => v == "WEBSITE_HOSTNAME")))
                .Returns<string>(s => _hostNameDefault);

            var hostNameProvider = new HostNameProvider(environmentMock.Object);
            _generator = new LinuxAppServiceEventGenerator(loggerFactoryMock, hostNameProvider, _functionsHostingConfigOptions, writer);
        }

        public static string UnNormalize(string normalized)
        {
            // We replace all double quotes to single before the writing the logs
            // to avoid our logging agents parsing break
            // TODO: we can remove this once platform is able to handle quotes in logs
            return normalized.Replace("'", "\"");
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetLogEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseLogEvents(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId, string runtimeSiteName, string slotName)
        {
            _generator.LogFunctionTraceEvent(level, subscriptionId, appName, functionName, eventName, source, details, summary, exceptionType, exceptionMessage, functionInvocationId, hostInstanceId, activityId, runtimeSiteName, slotName, DateTime.UtcNow);

            var logger = _generator.FunctionsLogsCategoryLogger as MockLinuxAppServiceFileLogger;
            var evt = logger.Events.Single();

            var regex = new Regex(LinuxAppServiceEventGenerator.TraceEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(17, match.Groups.Count);

            DateTime dt;
            var groupMatches = match.Groups.Cast<Group>().Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal((int)LinuxEventGenerator.ToEventLevel(level), int.Parse(p)),
                p => Assert.Equal(subscriptionId, p),
                p => Assert.Equal(_hostNameDefault, p),
                p => Assert.Equal(appName, p),
                p => Assert.Equal(functionName, p),
                p => Assert.Equal(eventName, p),
                p => Assert.Equal(source, p),
                p => Assert.Equal(details, LinuxContainerEventGeneratorTests.UnNormalize(p)),
                p => Assert.Equal(summary, LinuxContainerEventGeneratorTests.UnNormalize(p)),
                p => Assert.Equal(ScriptHost.Version, p),
                p => Assert.True(DateTime.TryParse(p, out dt)),
                p => Assert.Equal(exceptionType, p),
                p => Assert.Equal(exceptionMessage, LinuxContainerEventGeneratorTests.UnNormalize(p)),
                p => Assert.Equal(functionInvocationId, p),
                p => Assert.Equal(hostInstanceId, p),
                p => Assert.Equal(activityId, p));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetMetricEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseMetricEvents(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count, string data, string runtimeSiteName, string slotName)
        {
            _generator.LogFunctionMetricEvent(subscriptionId, appName, functionName, eventName, average, minimum, maximum, count, DateTime.Now, data, runtimeSiteName, slotName);

            var logger = _generator.FunctionsMetricsCategoryLogger as MockLinuxAppServiceFileLogger;
            var evt = logger.Events.Single();

            Regex regex = new Regex(LinuxAppServiceEventGenerator.MetricEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(12, match.Groups.Count);

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
                p => Assert.Equal(data, p));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetDetailsEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseDetailsEvents(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            _generator.LogFunctionDetailsEvent(siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);

            var logger = _generator.FunctionsDetailsCategoryLogger as MockLinuxAppServiceFileLogger;
            var evt = logger.Events.Single();

            Regex regex = new Regex(LinuxAppServiceEventGenerator.DetailsEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(7, match.Groups.Count);

            var groupMatches = match.Groups.Cast<Group>().Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal(siteName, p),
                p => Assert.Equal(functionName, p),
                p => Assert.Equal(inputBindings, LinuxContainerEventGeneratorTests.UnNormalize(p)),
                p => Assert.Equal(outputBindings, LinuxContainerEventGeneratorTests.UnNormalize(p)),
                p => Assert.Equal(scriptType, p),
                p => Assert.Equal(isDisabled ? "1" : "0", p));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetAzureMonitorEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseAzureMonitoringEvents(LogLevel level, string resourceId, string operationName, string category, string regionName, string properties)
        {
            _generator.LogAzureMonitorDiagnosticLogEvent(level, resourceId, operationName, category, regionName, properties);

            string evt = _events.Single();

            Regex regex = new Regex(LinuxAppServiceEventGenerator.AzureMonitorEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(8, match.Groups.Count);

            var groupMatches = match.Groups.Cast<Group>().Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal((int)LinuxEventGenerator.ToEventLevel(level), int.Parse(p)),
                p => Assert.Equal(resourceId, p),
                p => Assert.Equal(operationName, p),
                p => Assert.Equal(category, p),
                p => Assert.Equal(regionName, p),
                p => Assert.Equal(properties, UnNormalize(p)),
                p => Assert.True(DateTime.TryParse(p, out DateTime dt)));
        }

        [Theory]
        [MemberData(nameof(LinuxEventGeneratorTestData.GetFunctionExecutionEvents), MemberType = typeof(LinuxEventGeneratorTestData))]
        public void ParseFunctionExecutionEvents(string executionId, string siteName, int concurrency, string functionName, string invocationId,
            string executionStage, long executionTimeSpan, bool success, bool detailedExecutionEventsDisabled)
        {
            if (detailedExecutionEventsDisabled)
            {
                _functionsHostingConfigOptions.Value.DisableLinuxAppServiceExecutionDetails = true;
            }
            else
            {
                _functionsHostingConfigOptions.Value.DisableLinuxAppServiceExecutionDetails = false;
            }
            _generator.LogFunctionExecutionEvent(executionId, siteName, concurrency, functionName, invocationId, executionStage, executionTimeSpan, success);
            var logger = _generator.FunctionsExecutionEventsCategoryLogger as MockLinuxAppServiceFileLogger;
            var evt = logger.Events.Single();

            if (!detailedExecutionEventsDisabled)
            {
                Regex regex = new Regex(LinuxAppServiceEventGenerator.ExecutionEventRegex);
                var match = regex.Match(evt);

                Assert.True(match.Success);
                Assert.Equal(10, match.Groups.Count);

                var groupMatches = match.Groups.Cast<Group>().Select(p => p.Value).Skip(1).ToArray();
                Assert.Collection(groupMatches,
                    p => Assert.Equal(executionId, p),
                    p => Assert.Equal(siteName, p),
                    p => Assert.Equal(concurrency.ToString(), p),
                    p => Assert.Equal(functionName, p),
                    p => Assert.Equal(invocationId, p),
                    p => Assert.Equal(executionStage, p),
                    p => Assert.Equal(executionTimeSpan.ToString(), p),
                    p => Assert.True(Convert.ToBoolean(p)),
                    p => Assert.True(DateTime.TryParse(p, out DateTime dt)));
            }
            else
            {
                Assert.True(DateTime.TryParse(evt, out DateTime dt));
            }
        }
    }
}
