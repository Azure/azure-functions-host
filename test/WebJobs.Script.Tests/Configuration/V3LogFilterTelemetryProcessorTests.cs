// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class V3LogFilterTelemetryProcessorTests
    {
        [Fact]
        public void Process_V3LogsEnabled_AllowsTelemetryToFlow()
        {
            // Arrange
            var nextProcessor = new Mock<ITelemetryProcessor>();
            var hostingConfigOptions = new Mock<IOptionsMonitor<FunctionsHostingConfigOptions>>();
            var configOptions = new FunctionsHostingConfigOptions();
            
            hostingConfigOptions.Setup(x => x.CurrentValue).Returns(configOptions);
            
            var processor = new V3LogFilterTelemetryProcessor(nextProcessor.Object, hostingConfigOptions.Object);
            var telemetry = new TraceTelemetry("Test message");
            
            // Act
            processor.Process(telemetry);
            
            // Assert
            nextProcessor.Verify(x => x.Process(telemetry), Times.Once);
        }

        [Fact]
        public void Process_V3LogsDisabled_FilterV3LogsNotSet_AllowsTelemetryToFlow()
        {
            // Arrange
            var nextProcessor = new Mock<ITelemetryProcessor>();
            var hostingConfigOptions = new Mock<IOptionsMonitor<FunctionsHostingConfigOptions>>();
            var configOptions = new FunctionsHostingConfigOptions();
            configOptions.Features["DisableV3Logs"] = "1"; // V3 logs disabled
            
            hostingConfigOptions.Setup(x => x.CurrentValue).Returns(configOptions);
            
            var processor = new V3LogFilterTelemetryProcessor(nextProcessor.Object, hostingConfigOptions.Object);
            var telemetry = new TraceTelemetry("Test message");
            
            // FilterV3LogsForKusto is not set, so this should flow through to AppInsights
            
            // Act
            processor.Process(telemetry);
            
            // Assert
            nextProcessor.Verify(x => x.Process(telemetry), Times.Once);
        }

        [Fact]
        public void Process_V3LogsDisabled_FilterV3LogsSet_BlocksTelemetry()
        {
            // Arrange
            var nextProcessor = new Mock<ITelemetryProcessor>();
            var hostingConfigOptions = new Mock<IOptionsMonitor<FunctionsHostingConfigOptions>>();
            var configOptions = new FunctionsHostingConfigOptions();
            configOptions.Features["DisableV3Logs"] = "1"; // V3 logs disabled
            
            hostingConfigOptions.Setup(x => x.CurrentValue).Returns(configOptions);
            
            var processor = new V3LogFilterTelemetryProcessor(nextProcessor.Object, hostingConfigOptions.Object);
            var telemetry = new TraceTelemetry("Test message");
            
            // Set the filter flag to indicate this is Kusto-destined telemetry
            V3LogFilterTelemetryProcessor.FilterV3LogsForKusto.Value = true;
            
            try
            {
                // Act
                processor.Process(telemetry);
                
                // Assert - telemetry should be blocked for Kusto
                nextProcessor.Verify(x => x.Process(It.IsAny<ITelemetry>()), Times.Never);
            }
            finally
            {
                // Cleanup
                V3LogFilterTelemetryProcessor.FilterV3LogsForKusto.Value = false;
            }
        }

        [Fact]
        public void Process_V3LogsEnabled_FilterV3LogsSet_AllowsTelemetryToFlow()
        {
            // Arrange
            var nextProcessor = new Mock<ITelemetryProcessor>();
            var hostingConfigOptions = new Mock<IOptionsMonitor<FunctionsHostingConfigOptions>>();
            var configOptions = new FunctionsHostingConfigOptions();
            // V3 logs enabled (default)
            
            hostingConfigOptions.Setup(x => x.CurrentValue).Returns(configOptions);
            
            var processor = new V3LogFilterTelemetryProcessor(nextProcessor.Object, hostingConfigOptions.Object);
            var telemetry = new TraceTelemetry("Test message");
            
            // Set the filter flag to indicate this is Kusto-destined telemetry
            V3LogFilterTelemetryProcessor.FilterV3LogsForKusto.Value = true;
            
            try
            {
                // Act
                processor.Process(telemetry);
                
                // Assert - telemetry should flow through even with filter set when V3 logs are enabled
                nextProcessor.Verify(x => x.Process(telemetry), Times.Once);
            }
            finally
            {
                // Cleanup
                V3LogFilterTelemetryProcessor.FilterV3LogsForKusto.Value = false;
            }
        }
    }
}