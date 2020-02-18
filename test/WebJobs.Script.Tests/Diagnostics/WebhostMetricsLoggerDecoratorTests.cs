// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class WebhostMetricsLoggerDecoratorTests
    {
        private readonly Mock<IMetricsLogger> _metricsLogger;
        private readonly IMetricsLogger _metricsLoggerDecorator;

        public WebhostMetricsLoggerDecoratorTests()
        {
            _metricsLogger = new Mock<IMetricsLogger>();
            _metricsLoggerDecorator = new NonDisposableMetricsLogger(_metricsLogger.Object);
        }

        [Fact]
        public void Decorator_BeginEventWithParams_Called()
        {
            string eventName = "blah";
            string functionName = "blah";
            string data = "moreBlah";
            _metricsLogger.Setup(a => a.BeginEvent(eventName, functionName, data));
            _metricsLoggerDecorator.BeginEvent(eventName, functionName, data);
            _metricsLogger.Verify(a => a.BeginEvent(eventName, functionName, data), Times.Once());
            _metricsLogger.Reset();
        }

        [Fact]
        public void Decorator_BeginEventWithMetricEvent_Called()
        {
            Guid invocationId = Guid.NewGuid();
            FunctionMetadata meta = new FunctionMetadata();
            FunctionStartedEvent evt = new FunctionStartedEvent(invocationId, meta);
            _metricsLogger.Setup(a => a.BeginEvent(evt));
            _metricsLoggerDecorator.BeginEvent(evt);
            _metricsLogger.Verify(a => a.BeginEvent(evt), Times.Once());
            _metricsLogger.Reset();
        }

        [Fact]
        public void Decorator_EndEventWithMetricEvent_Called()
        {
            Guid invocationId = Guid.NewGuid();
            FunctionMetadata meta = new FunctionMetadata();
            FunctionStartedEvent evt = new FunctionStartedEvent(invocationId, meta);
            _metricsLogger.Setup(a => a.EndEvent(evt));
            _metricsLoggerDecorator.EndEvent(evt);
            _metricsLogger.Verify(a => a.EndEvent(evt), Times.Once());
            _metricsLogger.Reset();
        }

        [Fact]
        public void Decorator_EndEventWithEventHandle_Called()
        {
            object eh = new object();
            _metricsLogger.Setup(a => a.EndEvent(eh));
            _metricsLoggerDecorator.EndEvent(eh);
            _metricsLogger.Verify(a => a.EndEvent(eh), Times.Once());
            _metricsLogger.Reset();
        }

        [Fact]
        public void Decorator_LogEventWithMetricEvent_Called()
        {
            Guid invocationId = Guid.NewGuid();
            FunctionMetadata meta = new FunctionMetadata();
            FunctionStartedEvent evt = new FunctionStartedEvent(invocationId, meta);
            _metricsLogger.Setup(a => a.LogEvent(evt));
            _metricsLoggerDecorator.LogEvent(evt);
            _metricsLogger.Verify(a => a.LogEvent(evt), Times.Once());
            _metricsLogger.Reset();
        }

        [Fact]
        public void Decorator_LogEventWithParams_Called()
        {
            string eventName = "blah";
            string functionName = "blah";
            string data = "moreBlah";
            _metricsLogger.Setup(a => a.LogEvent(eventName, functionName, data));
            _metricsLoggerDecorator.LogEvent(eventName, functionName, data);
            _metricsLogger.Verify(a => a.LogEvent(eventName, functionName, data), Times.Once());
            _metricsLogger.Reset();
        }
    }
}
