// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionInvokerBaseTests
    {
        private MockInvoker _invoker;
        private TestMetricsLogger _metricsLogger;
        private TestLoggerProvider _testLoggerProvider;

        public FunctionInvokerBaseTests()
        {
            _metricsLogger = new TestMetricsLogger();
            _testLoggerProvider = new TestLoggerProvider();

            var scriptHostConfiguration = new ScriptHostConfiguration
            {
                HostConfig = new JobHostConfiguration(),
                FileLoggingMode = FileLoggingMode.Always,
                FileWatchingEnabled = true
            };

            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_testLoggerProvider);
            scriptHostConfiguration.HostConfig.LoggerFactory = loggerFactory;

            scriptHostConfiguration.HostConfig.AddService<IMetricsLogger>(_metricsLogger);

            var eventManager = new ScriptEventManager();

            var host = new Mock<ScriptHost>(new NullScriptHostEnvironment(), eventManager, scriptHostConfiguration, null, null, null);
            host.CallBase = true;

            host.SetupGet(h => h.IsPrimary).Returns(true);

            var funcDescriptor = new FunctionDescriptor();
            var funcDescriptors = new Collection<FunctionDescriptor>();
            funcDescriptors.Add(funcDescriptor);
            host.SetupGet(h => h.Functions).Returns(funcDescriptors);

            var metadata = new FunctionMetadata
            {
                Name = "TestFunction"
            };
            _invoker = new MockInvoker(host.Object, _metricsLogger, metadata);
            funcDescriptor.Metadata = metadata;
            funcDescriptor.Invoker = _invoker;
            funcDescriptor.Name = metadata.Name;
        }

        [Fact]
        public void LogOnPrimaryHost_WritesExpectedLogs()
        {
            _testLoggerProvider.ClearAllLogMessages();

            _invoker.LogOnPrimaryHost("Test message", LogLevel.Information);

            Assert.Equal(1, _testLoggerProvider.GetAllLogMessages().Count());
            var log = _testLoggerProvider.GetAllLogMessages().ElementAt(0);
            Assert.Equal("Test message", log.FormattedMessage);
            Assert.Equal(LogLevel.Information, log.Level);
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
            var parameters = new object[] { "Test", _invoker.FunctionLogger, executionContext };
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

            // verify latency event
            var startLatencyEvent = _metricsLogger.EventsBegan[0];
            Assert.Equal($"{MetricEventNames.FunctionInvokeLatency}_testfunction", startLatencyEvent);
            var completedLatencyEvent = _metricsLogger.EventsEnded[0];
            Assert.Equal(startLatencyEvent, completedLatencyEvent);
        }

        [Fact]
        public async Task Invoke_EmitsExpectedDuration()
        {
            var executionContext = new ExecutionContext
            {
                InvocationId = Guid.NewGuid()
            };
            var parameters1 = new object[] { "Test", _invoker.FunctionLogger, executionContext, new InvocationData { Delay = 2000 } };
            var parameters2 = new object[] { "Test", _invoker.FunctionLogger, executionContext };

            Task invocation1 = _invoker.Invoke(parameters1);
            Task invocation2 = _invoker.Invoke(parameters2);

            await Task.WhenAll(invocation1, invocation2);

            Assert.Equal(2, _metricsLogger.MetricEventsBegan.Count);
            Assert.Equal(2, _metricsLogger.EventsBegan.Count);
            Assert.Equal(2, _metricsLogger.MetricEventsEnded.Count);
            Assert.Equal(2, _metricsLogger.EventsEnded.Count);
        }

        [Fact]
        public async Task Invoke_Failure_EmitsExpectedEvents()
        {
            var executionContext = new ExecutionContext
            {
                InvocationId = Guid.NewGuid()
            };
            var parameters = new object[] { "Test", _invoker.FunctionLogger, executionContext, new InvocationData { Throw = true } };
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

            // verify latency event
            var startLatencyEvent = _metricsLogger.EventsBegan[0];
            Assert.Equal($"{MetricEventNames.FunctionInvokeLatency}_testfunction", startLatencyEvent);
            var completedLatencyEvent = _metricsLogger.EventsEnded[0];
            Assert.Equal(startLatencyEvent, completedLatencyEvent);
        }

        private class MockInvoker : FunctionInvokerBase
        {
            private readonly FunctionInstanceLogger _fastLogger;

            public MockInvoker(ScriptHost host, IMetricsLogger metrics, FunctionMetadata metadata) : base(host, metadata)
            {
                _fastLogger = new FunctionInstanceLogger(
                    (name) => this.Host.GetFunctionOrNull(name),
                    metrics);
            }

            protected override async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
            {
                FunctionInstanceLogEntry item = new FunctionInstanceLogEntry
                {
                    FunctionInstanceId = context.ExecutionContext.InvocationId,
                    StartTime = DateTime.UtcNow,
                    FunctionName = this.Metadata.Name,
                    Properties = new Dictionary<string, object>()
                };
                await _fastLogger.AddAsync(item);

                InvocationData invocation = parameters.OfType<InvocationData>().FirstOrDefault() ?? new InvocationData();

                string error = "failed";
                try
                {
                    if (invocation.Throw)
                    {
                        throw new InvalidOperationException("Kaboom!");
                    }

                    await Task.Delay(invocation.Delay);
                    error = null; // success
                    return null;
                }
                finally
                {
                    item.EndTime = DateTime.UtcNow;
                    item.ErrorDetails = error;
                    await _fastLogger.AddAsync(item);
                }
            }
        }

        private class InvocationData
        {
            public int Delay { get; set; } = 500;

            public bool Throw { get; set; }
        }
    }
}
