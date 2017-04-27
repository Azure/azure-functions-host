// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionInvokerBaseTests
    {
        private MockInvoker _invoker;
        private TestMetricsLogger _metricsLogger;
        private TestTraceWriter _traceWriter;

        public FunctionInvokerBaseTests()
        {
            _metricsLogger = new TestMetricsLogger();
            _traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            var config = new ScriptHostConfiguration();
            config.HostConfig.AddService<IMetricsLogger>(_metricsLogger);
            var hostMock = new Mock<ScriptHost>(MockBehavior.Strict, new object[] { new NullScriptHostEnvironment(), config, null });
            hostMock.Object.TraceWriter = _traceWriter;

            var metadata = new FunctionMetadata
            {
                Name = "TestFunction"
            };
            _invoker = new MockInvoker(hostMock.Object, metadata);
        }

        [Fact]
        public void LogInvocationMetrics_EmitsExpectedEvents()
        {
            var metrics = new TestMetricsLogger();
            var metadata = new FunctionMetadata
            {
                Name = "TestFunction"
            };
            metadata.Bindings.Add(new BindingMetadata { Type = "httpTrigger" });
            metadata.Bindings.Add(new BindingMetadata { Type = "blob", Direction = BindingDirection.In });
            metadata.Bindings.Add(new BindingMetadata { Type = "blob", Direction = BindingDirection.Out });
            metadata.Bindings.Add(new BindingMetadata { Type = "table", Direction = BindingDirection.In });
            metadata.Bindings.Add(new BindingMetadata { Type = "table", Direction = BindingDirection.In });
            var invokeLatencyEvent = FunctionInvokerBase.LogInvocationMetrics(metrics, metadata);

            Assert.Equal($"{MetricEventNames.FunctionInvokeLatency}_testfunction", (string)invokeLatencyEvent);

            Assert.Equal(5, metrics.LoggedEvents.Count);
            Assert.Equal("function.binding.httptrigger", metrics.LoggedEvents[0]);
            Assert.Equal("function.binding.blob.in", metrics.LoggedEvents[1]);
            Assert.Equal("function.binding.blob.out", metrics.LoggedEvents[2]);
            Assert.Equal("function.binding.table.in", metrics.LoggedEvents[3]);
            Assert.Equal("function.binding.table.in", metrics.LoggedEvents[4]);
        }

        [Fact]
        public async Task Invoke_Success_EmitsExpectedEvents()
        {
            var executionContext = new ExecutionContext
            {
                InvocationId = Guid.NewGuid()
            };
            var parameters = new object[] { "Test", _traceWriter, executionContext };
            await _invoker.Invoke(parameters);

            Assert.Equal(1, _metricsLogger.MetricEventsBegan.Count);
            Assert.Equal(1, _metricsLogger.EventsBegan.Count);
            Assert.Equal(1, _metricsLogger.MetricEventsEnded.Count);
            Assert.Equal(1, _metricsLogger.EventsEnded.Count);

            // verify started event
            var startedEvent = (FunctionStartedEvent)_metricsLogger.MetricEventsBegan[0];
            Assert.Equal(executionContext.InvocationId, startedEvent.InvocationId);

            var completedStartEvent = (FunctionStartedEvent)_metricsLogger.MetricEventsEnded[0];
            Assert.Same(startedEvent, completedStartEvent);
            Assert.True(completedStartEvent.Success);
            var message = _traceWriter.Traces.Last().Message;
            Assert.True(Regex.IsMatch(message, $"Function completed \\(Success, Id={executionContext.InvocationId}, Duration=[0-9]*ms\\)"));

            // verify latency event
            var startLatencyEvent = _metricsLogger.EventsBegan[0];
            Assert.Equal($"{MetricEventNames.FunctionInvokeLatency}_testfunction", startLatencyEvent);
            var completedLatencyEvent = _metricsLogger.EventsEnded[0];
            Assert.Equal(startLatencyEvent, completedLatencyEvent);
        }

        [Fact]
        public async Task Invoke_Failure_EmitsExpectedEvents()
        {
            var executionContext = new ExecutionContext
            {
                InvocationId = Guid.NewGuid()
            };
            var parameters = new object[] { "Test", _traceWriter, executionContext };
            _invoker.Throw = true;
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _invoker.Invoke(parameters);
            });

            Assert.Equal(1, _metricsLogger.MetricEventsBegan.Count);
            Assert.Equal(1, _metricsLogger.EventsBegan.Count);
            Assert.Equal(1, _metricsLogger.MetricEventsEnded.Count);
            Assert.Equal(1, _metricsLogger.EventsEnded.Count);

            // verify started event
            var startedEvent = (FunctionStartedEvent)_metricsLogger.MetricEventsBegan[0];
            Assert.Equal(executionContext.InvocationId, startedEvent.InvocationId);

            var completedStartEvent = (FunctionStartedEvent)_metricsLogger.MetricEventsEnded[0];
            Assert.Same(startedEvent, completedStartEvent);
            Assert.False(completedStartEvent.Success);
            var message = _traceWriter.Traces.Last().Message;
            Assert.True(Regex.IsMatch(message, $"Function completed \\(Failure, Id={executionContext.InvocationId}, Duration=[0-9]*ms\\)"));

            // verify latency event
            var startLatencyEvent = _metricsLogger.EventsBegan[0];
            Assert.Equal($"{MetricEventNames.FunctionInvokeLatency}_testfunction", startLatencyEvent);
            var completedLatencyEvent = _metricsLogger.EventsEnded[0];
            Assert.Equal(startLatencyEvent, completedLatencyEvent);
        }

        private class MockInvoker : FunctionInvokerBase
        {
            public MockInvoker(ScriptHost host, FunctionMetadata metadata, ITraceWriterFactory traceWriterFactory = null) : base(host, metadata, traceWriterFactory)
            {
            }

            public bool Throw { get; set; }

            protected override async Task InvokeCore(object[] parameters, FunctionInvocationContext context)
            {
                if (Throw)
                {
                    throw new InvalidOperationException("Kaboom!");
                }
                await Task.Delay(500);
            }
        }
    }
}
