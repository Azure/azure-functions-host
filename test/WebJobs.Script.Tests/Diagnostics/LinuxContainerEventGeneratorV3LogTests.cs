// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class LinuxContainerEventGeneratorV3LogTests
    {
        private readonly List<string> _events;
        private readonly Mock<IEnvironment> _environment;
        private LinuxContainerEventGenerator _generator;

        public LinuxContainerEventGeneratorV3LogTests()
        {
            _events = new List<string>();
            _environment = new Mock<IEnvironment>();
            
            // Setup default environment
            _environment.Setup(e => e.GetEnvironmentVariable(It.IsAny<string>())).Returns((string)null);
            
            _generator = new LinuxContainerEventGenerator(_environment.Object, (s) => _events.Add(s));
        }

        [Fact]
        public void LogFunctionTraceEvent_V3LogsDisabled_DoesNotLog()
        {
            // Arrange
            _environment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsDisableV3Logs)).Returns("1");

            // Act
            _generator.LogFunctionTraceEvent(LogLevel.Information, "sub1", "app1", "func1", "event1", "source1", 
                "details1", "summary1", "exception1", "exceptionMessage1", "invocation1", "host1", "activity1", 
                "runtime1", "slot1", DateTime.UtcNow);

            // Assert
            Assert.Empty(_events);
        }

        [Fact]
        public void LogFunctionTraceEvent_V3LogsEnabled_Logs()
        {
            // Arrange - V3 logs enabled by default (environment variable not set or set to 0)
            _environment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsDisableV3Logs)).Returns("0");

            // Act
            _generator.LogFunctionTraceEvent(LogLevel.Information, "sub1", "app1", "func1", "event1", "source1", 
                "details1", "summary1", "exception1", "exceptionMessage1", "invocation1", "host1", "activity1", 
                "runtime1", "slot1", DateTime.UtcNow);

            // Assert
            Assert.Single(_events);
        }

        [Fact]
        public void LogFunctionTraceEvent_V3LogsNotSet_Logs()
        {
            // Arrange - V3 logs enabled by default (environment variable not set)
            _environment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsDisableV3Logs)).Returns((string)null);

            // Act
            _generator.LogFunctionTraceEvent(LogLevel.Information, "sub1", "app1", "func1", "event1", "source1", 
                "details1", "summary1", "exception1", "exceptionMessage1", "invocation1", "host1", "activity1", 
                "runtime1", "slot1", DateTime.UtcNow);

            // Assert
            Assert.Single(_events);
        }

        [Fact]
        public void LogFunctionMetricEvent_V3LogsDisabled_DoesNotLog()
        {
            // Arrange
            _environment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsDisableV3Logs)).Returns("1");

            // Act
            _generator.LogFunctionMetricEvent("sub1", "app1", "func1", "event1", 100, 50, 150, 10, DateTime.UtcNow, "data1", "runtime1", "slot1");

            // Assert
            Assert.Empty(_events);
        }

        [Fact]
        public void LogFunctionDetailsEvent_V3LogsDisabled_DoesNotLog()
        {
            // Arrange
            _environment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsDisableV3Logs)).Returns("1");

            // Act
            _generator.LogFunctionDetailsEvent("site1", "func1", "input1", "output1", "scriptType1", false);

            // Assert
            Assert.Empty(_events);
        }
    }
}