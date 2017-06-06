// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class FilteringTelemetryProcessorTests
    {
        private Mock<ITelemetryProcessor> _nextTelemetryProcessorMock = new Mock<ITelemetryProcessor>(MockBehavior.Strict);

        public FilteringTelemetryProcessorTests()
        {
            _nextTelemetryProcessorMock
                .Setup(m => m.Process(It.IsAny<ITelemetry>()));
        }

        [Theory]
        [InlineData(LogLevel.None, LogLevel.Information, LogLevel.Information, true)]
        [InlineData(LogLevel.Trace, LogLevel.Trace, LogLevel.Information, true)]
        [InlineData(LogLevel.Error, LogLevel.Error, LogLevel.Information, false)]
        [InlineData(LogLevel.Trace, LogLevel.None, LogLevel.Information, false)]
        public void Processor_UsesFilter(LogLevel defaultLevel, LogLevel categoryLevel, LogLevel telemetryLevel, bool isEnabled)
        {
            var filter = new LogCategoryFilter
            {
                DefaultLevel = defaultLevel
            };

            filter.CategoryLevels[LogCategories.Results] = categoryLevel;

            var processor = new FilteringTelemetryProcessor(filter.Filter, _nextTelemetryProcessorMock.Object);

            var telemetry = new TestTelemetry();
            telemetry.Properties[LoggingKeys.CategoryName] = LogCategories.Results;
            telemetry.Properties[LoggingKeys.LogLevel] = telemetryLevel.ToString();

            processor.Process(telemetry);

            if (isEnabled)
            {
                _nextTelemetryProcessorMock.Verify(m => m.Process(telemetry), Times.Once);
            }
            else
            {
                _nextTelemetryProcessorMock.Verify(m => m.Process(It.IsAny<ITelemetry>()), Times.Never);
            }
        }

        [Fact]
        public void Processor_No_ISupportProperties_DoesNotFilter()
        {
            var filter = new LogCategoryFilter();

            var processor = new FilteringTelemetryProcessor(filter.Filter, _nextTelemetryProcessorMock.Object);

            var telemetry = new Mock<ITelemetry>(MockBehavior.Strict);

            processor.Process(telemetry.Object);
            _nextTelemetryProcessorMock.Verify(m => m.Process(telemetry.Object), Times.Once);
        }

        [Theory]
        [InlineData(LogLevel.Trace, false)]
        [InlineData(LogLevel.Information, true)]
        [InlineData(LogLevel.Error, true)]
        public void Processor_MissingCategory_FiltersAsDefault(LogLevel telemetryLevel, bool isEnabled)
        {
            var filter = new LogCategoryFilter
            {
                DefaultLevel = LogLevel.Information
            };

            var processor = new FilteringTelemetryProcessor(filter.Filter, _nextTelemetryProcessorMock.Object);

            var telemetry = new TestTelemetry();
            telemetry.Properties[LoggingKeys.LogLevel] = telemetryLevel.ToString();
            // no category specified

            processor.Process(telemetry);

            if (isEnabled)
            {
                _nextTelemetryProcessorMock.Verify(m => m.Process(telemetry), Times.Once);
            }
            else
            {
                _nextTelemetryProcessorMock.Verify(m => m.Process(It.IsAny<ITelemetry>()), Times.Never);
            }
        }

        [Fact]
        public void Processor_MissingLogLevel_DoesNotFilter()
        {
            // If no loglevel, we're not sure what to do, so just let it through
            var filter = new LogCategoryFilter
            {
                DefaultLevel = LogLevel.Information
            };

            var processor = new FilteringTelemetryProcessor(filter.Filter, _nextTelemetryProcessorMock.Object);

            var telemetry = new TestTelemetry();
            telemetry.Properties[LoggingKeys.CategoryName] = LogCategories.Results;
            // no log level specified

            processor.Process(telemetry);
            _nextTelemetryProcessorMock.Verify(m => m.Process(telemetry), Times.Once);
        }

        [Fact]
        public void Processor_InvalidLogLevel_DoesNotFilter()
        {
            // If no valid loglevel, we're not sure what to do, so just let it through
            var filter = new LogCategoryFilter
            {
                DefaultLevel = LogLevel.Information
            };

            var processor = new FilteringTelemetryProcessor(filter.Filter, _nextTelemetryProcessorMock.Object);

            var telemetry = new TestTelemetry();
            telemetry.Properties[LoggingKeys.CategoryName] = LogCategories.Results;
            telemetry.Properties[LoggingKeys.LogLevel] = "InvalidLevel";

            processor.Process(telemetry);
            _nextTelemetryProcessorMock.Verify(m => m.Process(telemetry), Times.Once);
        }

        private class TestTelemetry : ISupportProperties, ITelemetry
        {
            public DateTimeOffset Timestamp { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public TelemetryContext Context => throw new NotImplementedException();

            public string Sequence { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

            public void Sanitize()
            {
                throw new NotImplementedException();
            }

            public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();

        }
    }
}
