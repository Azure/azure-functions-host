// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Tests.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionDispatcherShutdownManagerTests
    {
        private ILoggerFactory _loggerFactory;
        private ILogger<FunctionDispatcherShutdownManager> _logger;

        public FunctionDispatcherShutdownManagerTests()
        {
            _loggerFactory = new LoggerFactory();
            _logger = _loggerFactory.CreateLogger<FunctionDispatcherShutdownManager>();
        }

        [Fact]
        public async Task Test_StopAsync()
        {
            Mock<IFunctionDispatcher> functionDispatcher = new Mock<IFunctionDispatcher>();
            functionDispatcher.Setup(a => a.ShutdownAsync()).Returns(Task.CompletedTask);
            var functionDispatcherShutdownManager = new FunctionDispatcherShutdownManager(functionDispatcher.Object, _logger);
            await functionDispatcherShutdownManager.StopAsync(CancellationToken.None);
            functionDispatcher.Verify(a => a.ShutdownAsync(), Times.Once);
        }

        [Fact]
        public async Task Test_StopAsync_Timesout()
        {
            Mock<IFunctionDispatcher> functionDispatcher = new Mock<IFunctionDispatcher>();
            functionDispatcher.Setup(a => a.ShutdownAsync()).Returns(new Task<bool>(() => true));   // A task that never starts and therefore never runs to completion
            var functionDispatcherShutdownManager = new FunctionDispatcherShutdownManager(functionDispatcher.Object, _logger);
            await functionDispatcherShutdownManager.StopAsync(CancellationToken.None);
            Assert.NotEqual(functionDispatcher.Object.ShutdownAsync().Status, TaskStatus.RanToCompletion);
        }

        [Fact]
        public async Task Test_StopAsync_WithInfinitelyRunningTask()
        {
            int expectedProcessCount = 2;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(LanguageWorkerConstants.NodeLanguageWorkerName));
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);

            foreach (var currChannel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
            {
                var initializedChannel = (TestLanguageWorkerChannel)currChannel;
                initializedChannel.ExecutionContexts.Add(Task.Factory.StartNew(() =>
                {
                    while (true)
                    {
                    }
                }));
            }
            var functionDispatcherShutdownManager = new FunctionDispatcherShutdownManager(functionDispatcher, _logger);
            await functionDispatcherShutdownManager.StopAsync(CancellationToken.None);
            foreach (var currChannel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
            {
                Assert.True(((TestLanguageWorkerChannel)currChannel).ExecutionContexts.Count > 0);
            }
        }

        private async Task<int> WaitForJobhostWorkerChannelsToStartup(RpcFunctionInvocationDispatcher functionDispatcher, int expectedCount)
        {
            int currentChannelCount = 0;
            await TestHelpers.Await(() =>
            {
                currentChannelCount = functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Count();
                if (currentChannelCount == expectedCount)
                {
                    return functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().All(ch => ch.State == LanguageWorkerChannelState.Initialized);
                }
                return false;
            }, pollingInterval: expectedCount * 5 * 1000, timeout: 60 * 1000);
            return currentChannelCount;
        }

        private IEnumerable<FunctionMetadata> GetTestFunctionsList(string runtime)
        {
            return new List<FunctionMetadata>()
            {
                new FunctionMetadata()
                {
                     Language = runtime,
                     Name = "js1"
                },

                new FunctionMetadata()
                {
                     Language = runtime,
                     Name = "js2"
                }
            };
        }

        private RpcFunctionInvocationDispatcher GetTestFunctionDispatcher(string maxProcessCountValue = null, bool addWebhostChannel = false, Mock<IWebHostLanguageWorkerChannelManager> mockwebHostLanguageWorkerChannelManager = null, bool throwOnProcessStartUp = false)
        {
            var eventManager = new ScriptEventManager();
            var scriptJobHostEnvironment = new Mock<IScriptJobHostEnvironment>();
            var metricsLogger = new Mock<IMetricsLogger>();
            var testEnv = new TestEnvironment();

            if (!string.IsNullOrEmpty(maxProcessCountValue))
            {
                testEnv.SetEnvironmentVariable(LanguageWorkerConstants.FunctionsWorkerProcessCountSettingName, maxProcessCountValue);
            }

            var loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();

            var options = new ScriptJobHostOptions
            {
                RootLogPath = Path.GetTempPath()
            };

            IOptions<ScriptJobHostOptions> scriptOptions = new OptionsManager<ScriptJobHostOptions>(new TestOptionsFactory<ScriptJobHostOptions>(options));

            var workerConfigOptions = new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
            IRpcWorkerChannelFactory testLanguageWorkerChannelFactory = new TestLanguageWorkerChannelFactory(eventManager, _logger, scriptOptions.Value.RootScriptPath, throwOnProcessStartUp);
            IWebHostLanguageWorkerChannelManager testWebHostLanguageWorkerChannelManager = new TestLanguageWorkerChannelManager(eventManager, _logger, scriptOptions.Value.RootScriptPath, testLanguageWorkerChannelFactory);
            IJobHostLanguageWorkerChannelManager jobHostLanguageWorkerChannelManager = new JobHostLanguageWorkerChannelManager(loggerFactory);
            if (addWebhostChannel)
            {
                testWebHostLanguageWorkerChannelManager.InitializeChannelAsync("java");
            }
            if (mockwebHostLanguageWorkerChannelManager != null)
            {
                testWebHostLanguageWorkerChannelManager = mockwebHostLanguageWorkerChannelManager.Object;
            }
            var mockFunctionDispatcherLoadBalancer = new Mock<IFunctionDispatcherLoadBalancer>();

            return new RpcFunctionInvocationDispatcher(scriptOptions,
                metricsLogger.Object,
                testEnv,
                scriptJobHostEnvironment.Object,
                eventManager,
                loggerFactory,
                testLanguageWorkerChannelFactory,
                new OptionsWrapper<LanguageWorkerOptions>(workerConfigOptions),
                testWebHostLanguageWorkerChannelManager,
                jobHostLanguageWorkerChannelManager,
                null,
                mockFunctionDispatcherLoadBalancer.Object);
        }
    }
}
