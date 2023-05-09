// Copyright (c) .NET Foundation. All rights reserved.
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
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionInvokerBaseTests : IDisposable
    {
        private MockInvoker _invoker;
        private IHost _host;
        private ScriptHost _scriptHost;
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
                Language = "node"
            };
            JObject binding = JObject.FromObject(new
            {
                type = "manualTrigger",
                name = "manual",
                direction = "in"
            });
            metadata.Bindings.Add(BindingMetadata.Create(binding));

            var metadataManager = new MockMetadataManager(new[] { metadata });
            _host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureServices(s =>
                {
                    s.AddSingleton<IFunctionMetadataManager>(metadataManager);
                })
                .Build();

            _scriptHost = _host.GetScriptHost();
            _scriptHost.InitializeAsync().Wait();

            _invoker = new MockInvoker(_scriptHost, _metricsLogger, metadataManager, metadata, loggerFactory);
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
            var startedEvent = (FunctionStartedEvent)_metricsLogger.MetricEventsBegan.ElementAt(0);
            Assert.Equal(executionContext.InvocationId, startedEvent.InvocationId);

            var completedStartEvent = (FunctionStartedEvent)_metricsLogger.MetricEventsEnded.ElementAt(0);
            Assert.Same(startedEvent, completedStartEvent);
            Assert.True(completedStartEvent.Success);
            Assert.Equal(startedEvent.FunctionName, "TestFunction");

            // verify invoke failed event
            Assert.False(string.IsNullOrEmpty(_metricsLogger.LoggedEvents.FirstOrDefault(e => e == $"{MetricEventNames.FunctionInvokeSucceeded}_testfunction")));

            // verify latency event
            var startLatencyEvent = _metricsLogger.EventsBegan.ElementAt(0);
            Assert.Equal($"{MetricEventNames.FunctionInvokeLatency}_testfunction", startLatencyEvent);
            var completedLatencyEvent = _metricsLogger.EventsEnded.ElementAt(0);
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
            var startedEvent = (FunctionStartedEvent)_metricsLogger.MetricEventsBegan.ElementAt(0);
            Assert.Equal(executionContext.InvocationId, startedEvent.InvocationId);

            var completedStartEvent = (FunctionStartedEvent)_metricsLogger.MetricEventsEnded.ElementAt(0);
            Assert.Same(startedEvent, completedStartEvent);
            Assert.False(completedStartEvent.Success);
            Assert.Equal(startedEvent.FunctionName, "TestFunction");

            // verify invoke failed event

            Assert.False(string.IsNullOrEmpty(_metricsLogger.LoggedEvents.FirstOrDefault(e => e == $"{MetricEventNames.FunctionInvokeFailed}_testfunction")));

            // verify latency event
            var startLatencyEvent = _metricsLogger.EventsBegan.ElementAt(0);
            Assert.Equal($"{MetricEventNames.FunctionInvokeLatency}_testfunction", startLatencyEvent);
            var completedLatencyEvent = _metricsLogger.EventsEnded.ElementAt(0);
            Assert.Equal(startLatencyEvent, completedLatencyEvent);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _host?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private class MockInvoker : FunctionInvokerBase
        {
            private readonly FunctionInstanceLogger _instanceLogger;

            public MockInvoker(ScriptHost host, IMetricsLogger metrics, IFunctionMetadataManager functionMetadataManager, FunctionMetadata metadata, ILoggerFactory loggerFactory)
                : base(host, metadata, loggerFactory)
            {
                _instanceLogger = new FunctionInstanceLogger(functionMetadataManager, metrics);
            }

            protected override async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
            {
                var item = new FunctionInstanceLogEntry
                {
                    FunctionInstanceId = context.ExecutionContext.InvocationId,
                    StartTime = DateTime.UtcNow,
                    FunctionName = Metadata.Name,
                    LogName = Utility.GetFunctionShortName(Metadata.Name),
                    Properties = new Dictionary<string, object>()
                };
                await _instanceLogger.AddAsync(item);

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
                    await _instanceLogger.AddAsync(item);
                }
            }
        }

        private class InvocationData
        {
            public int Delay { get; set; } = 500;

            public bool Throw { get; set; }
        }

        internal class MockMetadataManager : IFunctionMetadataManager
        {
            private readonly ICollection<FunctionMetadata> _functions;

            public MockMetadataManager(ICollection<FunctionMetadata> functions)
            {
                _functions = functions;
            }

            public ImmutableDictionary<string, ImmutableArray<string>> Errors =>
                ImmutableDictionary<string, ImmutableArray<string>>.Empty;

            public ImmutableArray<FunctionMetadata> GetFunctionMetadata(bool forceRefresh = false, bool applyAllowlist = true, bool includeCustomProviders = true, IList<RpcWorkerConfig> workerConfigs = null)
            {
                return _functions.ToImmutableArray();
            }

            public List<FunctionMetadata> GetValidMetadata(List<FunctionMetadata> functions)
            {
                throw new NotImplementedException();
            }

            public bool TryGetFunctionMetadata(string functionName, out FunctionMetadata functionMetadata, bool forceRefresh)
            {
                functionMetadata = _functions.FirstOrDefault(p => Utility.FunctionNamesMatch(p.Name, functionName));
                return functionMetadata != null;
            }
        }
    }
}
