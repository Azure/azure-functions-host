// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class FunctionDispatcherTests
    {
        [Theory]
        [InlineData("node", "node")]
        [InlineData("java", "java")]
        [InlineData("", "node")]
        [InlineData(null, "java")]
        public void IsSupported_Returns_True(string language, string funcMetadataLanguage)
        {
            IFunctionDispatcher functionDispatcher = GetTestFunctionDispatcher();
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = funcMetadataLanguage
            };
            Assert.True(functionDispatcher.IsSupported(func1, language));
        }

        [Theory]
        [InlineData("node", "java")]
        [InlineData("java", "node")]
        [InlineData("python", "")]
        public void IsSupported_Returns_False(string language, string funcMetadataLanguage)
        {
            IFunctionDispatcher functionDispatcher = GetTestFunctionDispatcher();
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = funcMetadataLanguage
            };
            Assert.False(functionDispatcher.IsSupported(func1, language));
        }

        [Fact]
        public async void Starting_MultipleJobhostChannels_Succeeds()
        {
            int expectedProcessCount = 3;
            FunctionDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(LanguageWorkerConstants.NodeLanguageWorkerName));

            var finalChannelCount = await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            Assert.Equal(expectedProcessCount, finalChannelCount);
        }

        [Fact]
        public async void Starting_MultipleWebhostChannels_Succeeds()
        {
            int expectedProcessCount = 2;
            FunctionDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString(), true);
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(LanguageWorkerConstants.JavaLanguageWorkerName));

            var finalWebhostChannelCount = await WaitForWebhostWorkerChannelsToStartup(functionDispatcher.WebHostLanguageWorkerChannelManager, expectedProcessCount, "java");
            Assert.Equal(expectedProcessCount, finalWebhostChannelCount);

            var finalJobhostChannelCount = functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Count();
            Assert.Equal(0, finalJobhostChannelCount);
        }

        [Fact]
        public void MaxProcessCount_Returns_Default()
        {
            FunctionDispatcher functionDispatcher = GetTestFunctionDispatcher();
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
            FunctionDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            Assert.Equal(expectedProcessCount, functionDispatcher.MaxProcessCount);
        }

        [Fact]
        public void MaxProcessCount_ProcessCount_Set_ExceedsMax_Returns_ExpectedCount()
        {
            int expectedProcessCount = 30;
            FunctionDispatcher functionDispatcher = GetTestFunctionDispatcher(expectedProcessCount.ToString());
            Assert.Equal(10, functionDispatcher.MaxProcessCount);
        }

        [Fact]
        public async void FunctionDispatcherState_Default_DotNetFunctions()
        {
            FunctionDispatcher functionDispatcher = GetTestFunctionDispatcher();
            Assert.Equal(FunctionDispatcherState.Default, functionDispatcher.State);
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
            Assert.Equal(FunctionDispatcherState.Default, functionDispatcher.State);

            await functionDispatcher.InitializeAsync(functions);
            Assert.Equal(FunctionDispatcherState.Default, functionDispatcher.State);

            await functionDispatcher.InitializeAsync(functions);
            Assert.Equal(FunctionDispatcherState.Default, functionDispatcher.State);
        }

        [Fact]
        public async void FunctionDispatcherState_Default_NoFunctions()
        {
            FunctionDispatcher functionDispatcher = GetTestFunctionDispatcher();
            Assert.Equal(FunctionDispatcherState.Default, functionDispatcher.State);
            await functionDispatcher.InitializeAsync(new List<FunctionMetadata>());
        }

        [Fact]
        public async void ShutdownChannels_NoFunctions()
        {
            var mockLanguageWorkerChannelManager = new Mock<IWebHostLanguageWorkerChannelManager>();
            mockLanguageWorkerChannelManager.Setup(m => m.ShutdownChannels());
            FunctionDispatcher functionDispatcher = GetTestFunctionDispatcher(mockwebHostLanguageWorkerChannelManager: mockLanguageWorkerChannelManager);
            Assert.Equal(FunctionDispatcherState.Default, functionDispatcher.State);
            await functionDispatcher.InitializeAsync(new List<FunctionMetadata>());
            // Wait longer than debouce action.
            await Task.Delay(6000);
            mockLanguageWorkerChannelManager.Verify(m => m.ShutdownChannels(), Times.Once);
        }

        [Fact]
        public async void ShutdownChannels_DotNetFunctions()
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
            var mockLanguageWorkerChannelManager = new Mock<IWebHostLanguageWorkerChannelManager>();
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher(mockwebHostLanguageWorkerChannelManager: mockLanguageWorkerChannelManager);
            Assert.Equal(FunctionDispatcherState.Default, functionDispatcher.State);
            await functionDispatcher.InitializeAsync(functions);
            // Wait longer than debouce action.
            await Task.Delay(6000);
            mockLanguageWorkerChannelManager.Verify(m => m.ShutdownChannels(), Times.Once);
        }

        [Fact]
        public async void FunctionDispatcherState_Transitions_From_Starting_To_Initialized()
        {
            FunctionDispatcher functionDispatcher = GetTestFunctionDispatcher();
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
            Assert.True(functionDispatcher.State == FunctionDispatcherState.Initializing || functionDispatcher.State == FunctionDispatcherState.Initialized);
            await WaitForFunctionDispactherStateInitialized(functionDispatcher);
        }

        [Fact]
        public async void FunctionDispatcher_Restart_ErroredChannels_Succeeds()
        {
            int expectedProcessCount = 2;
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(LanguageWorkerConstants.NodeLanguageWorkerName));

            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            int finalChannelCount = 0;
            for (int restartCount = 0; restartCount < expectedProcessCount * 3; restartCount++)
            {
                TestLanguageWorkerChannel testWorkerChannel = (TestLanguageWorkerChannel)functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().FirstOrDefault();
                if (functionDispatcher.LanguageWorkerErrors.Count < (expectedProcessCount * 3) - 1)
                {
                    testWorkerChannel.RaiseWorkerError();
                }
                finalChannelCount = await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            }
            Assert.Equal(expectedProcessCount, finalChannelCount);
        }

        [Fact]
        public async void FunctionDispatcher_Restart_ErroredChannels_ExcceedsLimit()
        {
            int expectedProcessCount = 2;
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(LanguageWorkerConstants.NodeLanguageWorkerName));

            await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            for (int restartCount = 0; restartCount < expectedProcessCount * 3; restartCount++)
            {
                foreach (var channel in functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels())
                {
                    TestLanguageWorkerChannel testWorkerChannel = channel as TestLanguageWorkerChannel;
                    testWorkerChannel.RaiseWorkerError();
                }
            }
            Assert.Equal(0, functionDispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Count());
        }

        private static FunctionDispatcher GetTestFunctionDispatcher(string maxProcessCountValue = null, bool addWebhostChannel = false, Mock<IWebHostLanguageWorkerChannelManager> mockwebHostLanguageWorkerChannelManager = null)
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
            var testLogger = new TestLogger("FunctionDispatcherTests");

            var options = new ScriptJobHostOptions
            {
                RootLogPath = Path.GetTempPath()
            };

            IOptions<ScriptJobHostOptions> scriptOptions = new OptionsManager<ScriptJobHostOptions>(new TestOptionsFactory<ScriptJobHostOptions>(options));

            var workerConfigOptions = new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
            IWebHostLanguageWorkerChannelManager testWebHostLanguageWorkerChannelManager = new TestLanguageWorkerChannelManager(eventManager, testLogger, scriptOptions.Value.RootScriptPath);
            ILanguageWorkerChannelFactory testLanguageWorkerChannelFactory = new TestLanguageWorkerChannelFactory(eventManager, testLogger, scriptOptions.Value.RootScriptPath);
            IJobHostLanguageWorkerChannelManager jobHostLanguageWorkerChannelManager = new JobHostLanguageWorkerChannelManager();
            if (addWebhostChannel)
            {
                testWebHostLanguageWorkerChannelManager.InitializeChannelAsync("java");
            }
            if (mockwebHostLanguageWorkerChannelManager != null)
            {
                testWebHostLanguageWorkerChannelManager = mockwebHostLanguageWorkerChannelManager.Object;
            }
            var mockFunctionDispatcherLoadBalancer = new Mock<IFunctionDispatcherLoadBalancer>();
            return new FunctionDispatcher(scriptOptions,
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

        private async Task<int> WaitForJobhostWorkerChannelsToStartup(FunctionDispatcher functionDispatcher, int expectedCount)
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

        private async Task<int> WaitForWebhostWorkerChannelsToStartup(IWebHostLanguageWorkerChannelManager channelManager, int expectedCount, string language)
        {
            int currentChannelCount = 0;
            await TestHelpers.Await(() =>
            {
                currentChannelCount = channelManager.GetChannels(language).Count();
                return currentChannelCount == expectedCount;
            }, pollingInterval: 4 * 1000, timeout: 60 * 1000);
            return currentChannelCount;
        }

        private async Task WaitForFunctionDispactherStateInitialized(IFunctionDispatcher functionDispatcher)
        {
            await TestHelpers.Await(() =>
            {
                return functionDispatcher.State == FunctionDispatcherState.Initialized;
            }, pollingInterval: 4 * 1000, timeout: 60 * 1000);
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
    }
}
