// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptJobHostOptionsTests
    {
        [Theory]
        [InlineData(FileLoggingMode.Always, 10, true, "Always", "00:10:00", "ApplicationInsights")]
        [InlineData(FileLoggingMode.Never, 5, false, "Never", "00:05:00", "OpenTelemetry")]
        [InlineData(FileLoggingMode.DebugOnly, 0.5, true, "DebugOnly", "00:00:30", "ApplicationInsights")]
        public void Format_SerializesOptionsToJson(
            FileLoggingMode loggingMode,
            double timeoutMinutes,
            bool fileWatchingEnabled,
            string expectedLoggingMode,
            string expectedTimeout,
            string expectedTelemetryMode)
        {
            var options = new ScriptJobHostOptions
            {
                FileLoggingMode = loggingMode,
                FunctionTimeout = TimeSpan.FromMinutes(timeoutMinutes),
                FileWatchingEnabled = fileWatchingEnabled,
                TelemetryMode = Enum.Parse<TelemetryMode>(expectedTelemetryMode)
            };

            string json = options.Format();
            var root = JsonDocument.Parse(json).RootElement;

            root.TryGetProperty("FileWatchingEnabled", out var fileWatchingProperty).Should().BeTrue();
            fileWatchingProperty.GetBoolean().Should().Be(fileWatchingEnabled);

            root.TryGetProperty("FileLoggingMode", out var fileLoggingProperty).Should().BeTrue();
            fileLoggingProperty.GetString().Should().Be(expectedLoggingMode);

            root.TryGetProperty("FunctionTimeout", out var timeoutProperty).Should().BeTrue();
            timeoutProperty.GetString().Should().Be(expectedTimeout);

            root.TryGetProperty("TelemetryMode", out var telemetryModeProperty).Should().BeTrue();
            telemetryModeProperty.GetString().Should().Be(expectedTelemetryMode);
        }

        [Fact]
        public void Format_WithNullFunctionTimeout_SerializesAsNull()
        {
            var options = new ScriptJobHostOptions
            {
                FileLoggingMode = FileLoggingMode.Never,
                FileWatchingEnabled = true,
                FunctionTimeout = null
            };

            string json = options.Format();
            var root = JsonDocument.Parse(json).RootElement;

            root.TryGetProperty("FunctionTimeout", out var timeoutProperty).Should().BeTrue();
            timeoutProperty.ValueKind.Should().Be(JsonValueKind.Null);
        }

        [Fact]
        public void Format_ReturnsValidIndentedJson()
        {
            var options = new ScriptJobHostOptions
            {
                FileLoggingMode = FileLoggingMode.Always,
                FileWatchingEnabled = true,
                FunctionTimeout = TimeSpan.FromMinutes(5)
            };

            string json = options.Format();

            // Should be valid JSON
            var exception = Record.Exception(() => JsonDocument.Parse(json));
            exception.Should().BeNull();

            // Should be indented (contains newlines)
            json.Should().Contain(Environment.NewLine);
        }
    }
}
