﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
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

            ILoggerFactory loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_testLoggerProvider);

            var eventManager = new ScriptEventManager();

            var metadata = new FunctionMetadata
            {
                Name = "TestFunction",
                ScriptFile = "index.js",
                ScriptType = ScriptType.Javascript
            };
            JObject binding = JObject.FromObject(new
            {
                type = "manualTrigger",
                name = "manual",
                direction = "in"
            });
            metadata.Bindings.Add(BindingMetadata.Create(binding));

            var host = new HostBuilder()
                .ConfigureDefaultTestScriptHost()
                .ConfigureServices(s =>
                {
                    var metadataManager = new MockMetadataManager(new[] { metadata });
                    s.AddSingleton<IFunctionMetadataManager>(metadataManager);

                    s.Configure<ScriptHostOptions>(o =>
                    {
                        // TODO DI: This should be set automatically
                        o.MaxMessageLengthBytes = ScriptHost.DefaultMaxMessageLengthBytesDynamicSku;
                    });
                })
                .Build();

            var scriptHost = host.GetScriptHost();
            scriptHost.InitializeAsync().Wait();

            _invoker = new MockInvoker(scriptHost, _metricsLogger, metadata, loggerFactory);
        }

        [Fact]
        public void LogOnPrimaryHost_WritesLogWithExpectedProperty()
        {
            _testLoggerProvider.ClearAllLogMessages();

            string guid = Guid.NewGuid().ToString();
            _invoker.LogOnPrimaryHost(guid, LogLevel.Information);

            var logMessage = _testLoggerProvider.GetAllLogMessages().Single(m => m.FormattedMessage.Contains(guid));
            Assert.Equal(LogLevel.Information, logMessage.Level);

            // Verify that the correct property is attached to the message. It's up to a Logger whether
            // they log messages with this value or not.
            Assert.Contains(logMessage.State, s => s.Key == ScriptConstants.LogPropertyPrimaryHostKey && (bool)s.Value == true);
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
            Assert.Equal("function.binding.httptrigger_testfunction", metrics.LoggedEvents[0]);
            Assert.Equal("function.binding.blob.in_testfunction", metrics.LoggedEvents[1]);
            Assert.Equal("function.binding.blob.out_testfunction", metrics.LoggedEvents[2]);
            Assert.Equal("function.binding.table.in_testfunction", metrics.LoggedEvents[3]);
            Assert.Equal("function.binding.table.in_testfunction", metrics.LoggedEvents[4]);
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

            public MockInvoker(ScriptHost host, IMetricsLogger metrics, FunctionMetadata metadata, ILoggerFactory loggerFactory)
                : base(host, metadata, loggerFactory)
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

        private class MockMetadataManager : IFunctionMetadataManager
        {
            private readonly ICollection<FunctionMetadata> _functions;

            public MockMetadataManager(ICollection<FunctionMetadata> functions)
            {
                _functions = functions;
            }

            public ImmutableDictionary<string, ImmutableArray<string>> Errors =>
                ImmutableDictionary<string, ImmutableArray<string>>.Empty;

            public ImmutableArray<FunctionMetadata> Functions => _functions.ToImmutableArray();
        }
    }
}
