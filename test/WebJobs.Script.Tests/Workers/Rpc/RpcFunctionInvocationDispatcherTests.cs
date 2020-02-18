// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Abstractions.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class RpcFunctionInvocationDispatcherTests
    {
        private static TestRpcWorkerChannel _javaTestChannel;
        private static TestLogger _testLogger = new TestLogger("FunctionDispatcherTests");

        [Fact]
        public async void Starting_MultipleJobhostChannels_Succeeds()
        {
            int expectedProcessCount = 3;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));

            var finalChannelCount = await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            Assert.Equal(expectedProcessCount, finalChannelCount);
        }

        [Fact]
        public async void Starting_MultipleWebhostChannels_Succeeds()
        {
            int expectedProcessCount = 2;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString(), true);
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.JavaLanguageWorkerName));

            var finalWebhostChannelCount = await WaitForWebhostWorkerChannelsToStartup(functionDispatcher.WebHostLanguageWorkerChannelManager, expectedProcessCount, "java");
            Assert.Equal(expectedProcessCount, finalWebhostChannelCount);

            var finalJobhostChannelCount = functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Count();
            Assert.Equal(0, finalJobhostChannelCount);
        }

        [Fact]
        public async void Restart_ParticularWorkerChannel_Succeeds_OnlyThatIsDisposed()
        {
            int expectedProcessCount = 3;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            Guid invocationId = Guid.NewGuid();
            List<TestRpcWorkerChannel> workerChannels = functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Cast<TestRpcWorkerChannel>().ToList();
            workerChannels[0].SendInvocationRequest(new ScriptInvocationContext
            {
                ExecutionContext = new ExecutionContext
                {
                    InvocationId = invocationId
                }
            });

            await functionDispatcher.RestartWorkerWithInvocationIdAsync(invocationId.ToString());
            Assert.True(workerChannels[0].IsDisposed);
            for (int i = 1; i < workerChannels.Count; ++i)
            {
                Assert.False(workerChannels[i].IsDisposed); // Ensure no other channel is disposed
            }

            Assert.Equal(expectedProcessCount, functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Count());   // Ensure count goes back to initial count
        }

        [Fact]
        public async void Restart_AllChannels_Succeeds()
        {
            int expectedProcessCount = 3;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            Guid invocationId = Guid.NewGuid();
            List<TestRpcWorkerChannel> workerChannels = functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Cast<TestRpcWorkerChannel>().ToList();
            await functionDispatcher.RestartAllWorkersAsync();
            foreach (TestRpcWorkerChannel channel in workerChannels)
            {
                Assert.True(channel.IsDisposed);
            }

            Assert.Equal(expectedProcessCount, functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Count());   // Ensure count goes back to initial count
        }

        [Fact]
        public async Task ShutdownTests()
        {
            int expectedProcessCount = 2;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);

            foreach (var currChannel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
            {
                var initializedChannel = (TestRpcWorkerChannel)currChannel;
                initializedChannel.ExecutionContexts.Add(Task.Factory.StartNew(() => { }));
            }

            await functionDispatcher.ShutdownAsync();
            foreach (var currChannel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
            {
                Assert.True(((TestRpcWorkerChannel)currChannel).ExecutionContexts.Count == 0);
            }
        }

        [Fact]
        public async Task ShutdownTests_WithInfinitelyRunningTasks_Timesout()
        {
            int expectedProcessCount = 2;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);

            foreach (var currChannel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
            {
                var initializedChannel = (TestRpcWorkerChannel)currChannel;
                initializedChannel.ExecutionContexts.Add(new Task<bool>(() => true));   // A task that never starts and therefore never runs to completion
            }

            await functionDispatcher.ShutdownAsync();
            foreach (var currChannel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
            {
                Assert.True(((TestRpcWorkerChannel)currChannel).ExecutionContexts.Count > 0);
            }
        }

        [Fact]
        public void MaxProcessCount_Returns_Default()
        {
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher();
            Assert.Equal(1, functionDispatcher.MaxProcessCount);

            functionDispatcher = GetTestFunctionDispatcher("0");
            Assert.Equal(1, functionDispatcher.MaxProcessCount);

            functionDispatcher = GetTestFunctionDispatcher("-1");
            Assert.Equal(1, functionDispatcher.MaxProcessCount);
        }

        [Fact]
        public void MaxProcessCount_ProcessCount_Set_Returns_ExpectedCount()
        {
            int expectedProcessCount = 3;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            Assert.Equal(expectedProcessCount, functionDispatcher.MaxProcessCount);
        }

        [Fact]
        public void MaxProcessCount_ProcessCount_Set_ExceedsMax_Returns_ExpectedCount()
        {
            int expectedProcessCount = 30;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            Assert.Equal(10, functionDispatcher.MaxProcessCount);
        }

        [Fact]
        public async Task FunctionDispatcherState_Default_DotNetFunctions()
        {
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher();
            Assert.Equal(FunctionInvocationDispatcherState.Default, functionDispatcher.State);
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = "dotnet"
            };
            var functions = new List<FunctionMetadata>()
            {
                func1
            };
            await functionDispatcher.InitializeAsync(functions);
            Assert.Equal(FunctionInvocationDispatcherState.Default, functionDispatcher.State);

            await functionDispatcher.InitializeAsync(functions);
            Assert.Equal(FunctionInvocationDispatcherState.Default, functionDispatcher.State);

            await functionDispatcher.InitializeAsync(functions);
            Assert.Equal(FunctionInvocationDispatcherState.Default, functionDispatcher.State);
        }

        [Fact]
        public async Task FunctionDispatcherState_Default_NoFunctions()
        {
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher();
            Assert.Equal(FunctionInvocationDispatcherState.Default, functionDispatcher.State);
            await functionDispatcher.InitializeAsync(new List<FunctionMetadata>());
        }

        [Fact]
        public async Task ShutdownChannels_NoFunctions()
        {
            var mockLanguageWorkerChannelManager = new Mock<IWebHostRpcWorkerChannelManager>();
            mockLanguageWorkerChannelManager.Setup(m => m.ShutdownChannelsAsync()).Returns(Task.CompletedTask);
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(mockwebHostLanguageWorkerChannelManager: mockLanguageWorkerChannelManager);
            Assert.Equal(FunctionInvocationDispatcherState.Default, functionDispatcher.State);
            await functionDispatcher.InitializeAsync(new List<FunctionMetadata>());
            // Wait longer than debouce action.
            await Task.Delay(6000);
            mockLanguageWorkerChannelManager.Verify(m => m.ShutdownChannelsAsync(), Times.Once);
        }

        [Fact]
        public async Task ShutdownChannels_DotNetFunctions()
        {
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = "dotnet"
            };
            var functions = new List<FunctionMetadata>()
            {
                func1
            };
            var mockLanguageWorkerChannelManager = new Mock<IWebHostRpcWorkerChannelManager>();
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(mockwebHostLanguageWorkerChannelManager: mockLanguageWorkerChannelManager);
            Assert.Equal(FunctionInvocationDispatcherState.Default, functionDispatcher.State);
            await functionDispatcher.InitializeAsync(functions);
            // Wait longer than debouce action.
            await Task.Delay(6000);
            mockLanguageWorkerChannelManager.Verify(m => m.ShutdownChannelsAsync(), Times.Once);
        }

        [Fact]
        public async Task FunctionDispatcherState_Transitions_From_Starting_To_Initialized()
        {
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher();
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = "node"
            };
            var functions = new List<FunctionMetadata>()
            {
                func1
            };
            await functionDispatcher.InitializeAsync(functions);
            Assert.Equal(FunctionInvocationDispatcherState.Initializing, functionDispatcher.State);
            await WaitForFunctionDispactherStateInitialized(functionDispatcher);
            Assert.Equal(FunctionInvocationDispatcherState.Initialized, functionDispatcher.State);
        }

        [Fact]
        public async Task FunctionDispatcherState_Transitions_From_Default_To_Initialized_To_Disposing()
        {
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher();
            Assert.Equal(FunctionInvocationDispatcherState.Default, functionDispatcher.State);
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = "node"
            };
            var functions = new List<FunctionMetadata>()
            {
                func1
            };
            await functionDispatcher.InitializeAsync(functions);
            Assert.Equal(FunctionInvocationDispatcherState.Initializing, functionDispatcher.State);
            await WaitForFunctionDispactherStateInitialized(functionDispatcher);
            Assert.Equal(FunctionInvocationDispatcherState.Initialized, functionDispatcher.State);
            functionDispatcher.Dispose();
            Assert.True(functionDispatcher == null || functionDispatcher.State == FunctionInvocationDispatcherState.Disposing || functionDispatcher.State == FunctionInvocationDispatcherState.Disposed);
        }

        [Fact]
        public async Task FunctionDispatcher_Restart_ErroredChannels_Succeeds()
        {
            int expectedProcessCount = 2;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));

            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            int finalChannelCount = 0;
            for (int restartCount = 0; restartCount < expectedProcessCount * 3; restartCount++)
            {
                TestRpcWorkerChannel testWorkerChannel = (TestRpcWorkerChannel)functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().FirstOrDefault();
                if (functionDispatcher.LanguageWorkerErrors.Count < (expectedProcessCount * 3) - 1)
                {
                    testWorkerChannel.RaiseWorkerError();
                }
                finalChannelCount = await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            }
            Assert.Equal(expectedProcessCount, finalChannelCount);
        }

        [Fact]
        public async Task FunctionDispatcher_Restart_ErroredChannels_And_Changes_State()
        {
            int expectedProcessCount = 1;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            Assert.Equal(FunctionInvocationDispatcherState.Default, functionDispatcher.State);
            // Add worker
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            TestRpcWorkerChannel testWorkerChannel = (TestRpcWorkerChannel)functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().FirstOrDefault();
            // Restart this channel
            testWorkerChannel.RaiseWorkerRestart();
            await TestHelpers.Await(() =>
            {
                return functionDispatcher.State == FunctionInvocationDispatcherState.WorkerProcessRestarting;
            }, 3000);
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            Assert.Equal(FunctionInvocationDispatcherState.Initialized, functionDispatcher.State);
        }

        [Fact]
        public async Task FunctionDispatcher_Restart_ErroredChannels_And_DoesNot_Change_State()
        {
            int expectedProcessCount = 2;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            Assert.Equal(FunctionInvocationDispatcherState.Default, functionDispatcher.State);
            // Add worker
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            TestRpcWorkerChannel testWorkerChannel = (TestRpcWorkerChannel)functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().FirstOrDefault();
            // Restart one channel
            testWorkerChannel.RaiseWorkerRestart();
            Assert.Equal(FunctionInvocationDispatcherState.Initialized, functionDispatcher.State);
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            Assert.Equal(FunctionInvocationDispatcherState.Initialized, functionDispatcher.State);
        }

        [Fact]
        public async Task FunctionDispatcher_Restart_ErroredChannels_ExceedsLimit()
        {
            int expectedProcessCount = 2;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));

            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            for (int restartCount = 0; restartCount < expectedProcessCount * 3; restartCount++)
            {
                foreach (var channel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
                {
                    TestRpcWorkerChannel testWorkerChannel = channel as TestRpcWorkerChannel;
                    testWorkerChannel.RaiseWorkerError();
                }
            }
            Assert.Equal(0, functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Count());
        }

        [Fact]
        public async Task FunctionDispatcher_Restart_ErroredChannels_OnWorkerRestart_NotAffectedByLimit()
        {
            int expectedProcessCount = 2;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));

            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            for (int restartCount = 0; restartCount < expectedProcessCount * 3; restartCount++)
            {
                foreach (var channel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
                {
                    TestRpcWorkerChannel testWorkerChannel = channel as TestRpcWorkerChannel;
                    testWorkerChannel.RaiseWorkerRestart();
                }

                var finalChannelCount = await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
                Assert.Equal(expectedProcessCount, finalChannelCount);
            }
        }

        [Fact]
        public async Task FunctionDispatcher_Error_WithinThreshold_BucketFills()
        {
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher("1");
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, 1);
            for (int i = 0; i < 3; ++i)
            {
                foreach (var channel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
                {
                    TestRpcWorkerChannel testWorkerChannel = channel as TestRpcWorkerChannel;
                    testWorkerChannel.RaiseWorkerError();
                }
            }

            Assert.Equal(3, functionDispatcher.LanguageWorkerErrors.Count);
        }

        [Fact]
        public async Task FunctionDispatcher_Error_BeyondThreshold_BucketIsAtOne()
        {
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher("1");
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, 1);
            for (int i = 1; i < 10; ++i)
            {
                foreach (var channel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
                {
                    TestRpcWorkerChannel testWorkerChannel = channel as TestRpcWorkerChannel;
                    testWorkerChannel.RaiseWorkerErrorWithCustomTimestamp(DateTime.UtcNow.AddHours(i));
                }
            }

            Assert.Equal(1, functionDispatcher.LanguageWorkerErrors.Count);
        }

        [Fact]
        public async Task FunctionDispatcher_DoNot_Restart_ErroredChannels_If_WorkerRuntime_DoesNotMatch()
        {
            int expectedProcessCount = 1;
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.NodeLanguageWorkerName));
            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            _javaTestChannel.RaiseWorkerError();
            var testLogs = _testLogger.GetLogMessages();
            Assert.False(testLogs.Any(m => m.FormattedMessage.Contains("Restarting worker channel for runtime:java")));
            Assert.Equal(expectedProcessCount, functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Count());
        }

        [Theory]
        [InlineData("node", "node", false, true, true)]
        [InlineData("node", "node", true, false, true)]
        [InlineData("node", "node", true, true, true)]
        [InlineData("node", "node", false, false, false)]
        [InlineData("node", "java", false, true, false)]
        [InlineData("node", "", false, true, false)]
        [InlineData(null, "", false, true, false)]
        [InlineData(null, "node", false, true, false)]
        [InlineData(null, "node", true, false, false)]
        [InlineData(null, "node", true, true, false)]
        [InlineData(null, "node", false, false, false)]
        [InlineData(null, "java", false, true, false)]
        public async Task FunctionDispatcher_ShouldRestartChannel_Returns_True(string workerRuntime, string channelLanguage, bool isWebHostChannel, bool isJobHostChannel, bool expectedResult)
        {
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher();
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(workerRuntime));
            Assert.Equal(expectedResult, functionDispatcher.ShouldRestartWorkerChannel(channelLanguage, isWebHostChannel, isJobHostChannel));
        }

        [Fact]
        public async Task FunctionDispatcher_ErroredWebHostChannel()
        {
            RpcFunctionInvocationDispatcher functionDispatcher = GetTestFunctionDispatcher(throwOnProcessStartUp: true, addWebhostChannel: true);
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(RpcWorkerConstants.JavaLanguageWorkerName));
            var testLogs = _testLogger.GetLogMessages();
            Assert.False(testLogs.Any(m => m.FormattedMessage.Contains("Removing errored webhost language worker channel for runtime")));
        }

        private static RpcFunctionInvocationDispatcher GetTestFunctionDispatcher(string maxProcessCountValue = null, bool addWebhostChannel = false, Mock<IWebHostRpcWorkerChannelManager> mockwebHostLanguageWorkerChannelManager = null, bool throwOnProcessStartUp = false)
        {
            var eventManager = new ScriptEventManager();
            var metricsLogger = new Mock<IMetricsLogger>();
            var mockApplicationLifetime = new Mock<IApplicationLifetime>();
            var testEnv = new TestEnvironment();

            if (!string.IsNullOrEmpty(maxProcessCountValue))
            {
                testEnv.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerProcessCountSettingName, maxProcessCountValue);
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
            IRpcWorkerChannelFactory testLanguageWorkerChannelFactory = new TestRpcWorkerChannelFactory(eventManager, _testLogger, scriptOptions.Value.RootScriptPath, throwOnProcessStartUp);
            IWebHostRpcWorkerChannelManager testWebHostLanguageWorkerChannelManager = new TesRpcWorkerChannelManager(eventManager, _testLogger, scriptOptions.Value.RootScriptPath, testLanguageWorkerChannelFactory);
            IJobHostRpcWorkerChannelManager jobHostLanguageWorkerChannelManager = new JobHostRpcWorkerChannelManager(loggerFactory);
            if (addWebhostChannel)
            {
                testWebHostLanguageWorkerChannelManager.InitializeChannelAsync("java");
            }
            if (mockwebHostLanguageWorkerChannelManager != null)
            {
                testWebHostLanguageWorkerChannelManager = mockwebHostLanguageWorkerChannelManager.Object;
            }
            var mockFunctionDispatcherLoadBalancer = new Mock<IRpcFunctionInvocationDispatcherLoadBalancer>();

            _javaTestChannel = new TestRpcWorkerChannel(Guid.NewGuid().ToString(), "java", eventManager, _testLogger, false);

            return new RpcFunctionInvocationDispatcher(scriptOptions,
                metricsLogger.Object,
                testEnv,
                mockApplicationLifetime.Object,
                eventManager,
                loggerFactory,
                testLanguageWorkerChannelFactory,
                new OptionsWrapper<LanguageWorkerOptions>(workerConfigOptions),
                testWebHostLanguageWorkerChannelManager,
                jobHostLanguageWorkerChannelManager,
                new OptionsWrapper<ManagedDependencyOptions>(new ManagedDependencyOptions()),
                mockFunctionDispatcherLoadBalancer.Object);
        }

        private async Task<int> WaitForJobhostWorkerChannelsToStartup(RpcFunctionInvocationDispatcher functionDispatcher, int expectedCount)
        {
            int currentChannelCount = 0;
            await TestHelpers.Await(() =>
            {
                currentChannelCount = functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Count();
                if (currentChannelCount == expectedCount)
                {
                    return functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().All(ch => ch.IsChannelReadyForInvocations());
                }
                return false;
            }, pollingInterval: expectedCount * 5 * 1000, timeout: 60 * 1000);
            return currentChannelCount;
        }

        private async Task<int> WaitForWebhostWorkerChannelsToStartup(IWebHostRpcWorkerChannelManager channelManager, int expectedCount, string language)
        {
            int currentChannelCount = 0;
            await TestHelpers.Await(() =>
            {
                currentChannelCount = channelManager.GetChannels(language).Count();
                return currentChannelCount == expectedCount;
            }, pollingInterval: 4 * 1000, timeout: 60 * 1000);
            return currentChannelCount;
        }

        private async Task WaitForFunctionDispactherStateInitialized(IFunctionInvocationDispatcher functionDispatcher)
        {
            await TestHelpers.Await(() =>
            {
                return functionDispatcher.State == FunctionInvocationDispatcherState.Initialized;
            }, pollingInterval: 4 * 1000, timeout: 60 * 1000);
        }

        private IEnumerable<FunctionMetadata> GetTestFunctionsList(string runtime)
        {
            if (string.IsNullOrEmpty(runtime))
            {
                return new List<FunctionMetadata>();
            }

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
    }
}