// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class LinuxContainerEventGeneratorTests
    {
        private readonly LinuxContainerEventGenerator _generator;
        private readonly List<string> _events;

        public LinuxContainerEventGeneratorTests()
        {
            _events = new List<string>();
            Action<string> writer = (s) =>
            {
                _events.Add(s);
            };
            _generator = new LinuxContainerEventGenerator(writer);
        }

        public static IEnumerable<object[]> GetLogEvents()
        {
            yield return new object[] { LogLevel.Information, "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction", "TestEvent", "TestSource", "These are the details, lots of details", "This is the summary, a great summary", "TestExceptionType", "Test exception message, with details", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53" };
            yield return new object[] { LogLevel.Information, string.Empty, "TestApp", "TestFunction", "TestEvent", string.Empty, "This string includes a quoted \"substring\" in the middle", "Another \"quoted substring\"", "TestExceptionType", "Another \"quoted substring\"", "E2D5A6ED-4CE3-4CFD-8878-FD4814F0A1F3", "3AD41658-1C4E-4C9D-B0B9-24F2BDAE2829", "F0AAA9AD-C3A6-48B9-A75E-57BB280EBB53" };
            yield return new object[] { LogLevel.Information, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty };
        }

        public static IEnumerable<object[]> GetMetricEvents()
        {
            yield return new object[] { "C37E3412-86D1-4B93-BC5A-A2AE09D26C2D", "TestApp", "TestFunction", "TestEvent", 15, 2, 18, 5 };
            yield return new object[] { string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, 0, 0 };
        }

        public static IEnumerable<object[]> GetDetailsEvents()
        {
            yield return new object[] { "TestApp", "TestFunction", "{ foo: 123, bar: \"Test\" }", "{ foo: \"Mathew\", bar: \"Charles\" }", "CSharp", 0 };
            yield return new object[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0 };
        }

        [Theory]
        [MemberData(nameof(GetLogEvents))]
        public void ParseLogEvents(LogLevel level, string subscriptionId, string appName, string functionName, string eventName, string source, string details, string summary, string exceptionType, string exceptionMessage, string functionInvocationId, string hostInstanceId, string activityId)
        {
            _generator.LogFunctionTraceEvent(level, subscriptionId, appName, functionName, eventName, source, details, summary, exceptionType, exceptionMessage, functionInvocationId, hostInstanceId, activityId);

            string evt = _events.Single();

            Regex regex = new Regex(LinuxContainerEventGenerator.TraceEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(16, match.Groups.Count);

            DateTime dt;
            var groupMatches = match.Groups.Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal(level.ToString(), p),
                p => Assert.Equal(subscriptionId, p),
                p => Assert.Equal(appName, p),
                p => Assert.Equal(functionName, p),
                p => Assert.Equal(eventName, p),
                p => Assert.Equal(source, p),
                p => Assert.Equal(details, p),
                p => Assert.Equal(summary, p),
                p => Assert.Equal(ScriptHost.Version, p),
                p => Assert.True(DateTime.TryParse(p, out dt)),
                p => Assert.Equal(exceptionType, p),
                p => Assert.Equal(exceptionMessage, p),
                p => Assert.Equal(functionInvocationId, p),
                p => Assert.Equal(hostInstanceId, p),
                p => Assert.Equal(activityId, p));
        }

        [Theory]
        [MemberData(nameof(GetMetricEvents))]
        public void ParseMetricEvents(string subscriptionId, string appName, string functionName, string eventName, long average, long minimum, long maximum, long count)
        {
            _generator.LogFunctionMetricEvent(subscriptionId, appName, functionName, eventName, average, minimum, maximum, count, DateTime.Now);

            string evt = _events.Single();

            Regex regex = new Regex(LinuxContainerEventGenerator.MetricEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(11, match.Groups.Count);

            DateTime dt;
            var groupMatches = match.Groups.Select(p => p.Value).Skip(1).ToArray();
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
                p => Assert.True(DateTime.TryParse(p, out dt)));
        }

        [Theory]
        [MemberData(nameof(GetDetailsEvents))]
        public void ParseDetailsEvents(string siteName, string functionName, string inputBindings, string outputBindings, string scriptType, bool isDisabled)
        {
            _generator.LogFunctionDetailsEvent(siteName, functionName, inputBindings, outputBindings, scriptType, isDisabled);

            string evt = _events.Single();

            Regex regex = new Regex(LinuxContainerEventGenerator.DetailsEventRegex);
            var match = regex.Match(evt);

            Assert.True(match.Success);
            Assert.Equal(7, match.Groups.Count);

            var groupMatches = match.Groups.Select(p => p.Value).Skip(1).ToArray();
            Assert.Collection(groupMatches,
                p => Assert.Equal(siteName, p),
                p => Assert.Equal(functionName, p),
                p => Assert.Equal(inputBindings, p),
                p => Assert.Equal(outputBindings, p),
                p => Assert.Equal(scriptType, p),
                p => Assert.Equal(isDisabled ? "1" : "0", p));
        }
    }
}
