// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
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
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher(expectedProcessCount.ToString());
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(LanguageWorkerConstants.NodeLanguageWorkerName));

            var finalChannelCount = await WaitForJobhostWorkerChannelsToStartup(functionDispatcher, expectedProcessCount);
            Assert.Equal(expectedProcessCount, finalChannelCount);

            // Verify LanguageWorkerChannelState when channel after it is initialized
            Assert.True(functionDispatcher.WorkerState.GetChannels().All(ch => ch.State == LanguageWorkerChannelState.Initialized));
        }

        [Fact]
        public async void Starting_MultipleWebhostChannels_Succeeds()
        {
            int expectedProcessCount = 2;
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher(expectedProcessCount.ToString(), true);
            await functionDispatcher.InitializeAsync(GetTestFunctionsList(LanguageWorkerConstants.JavaLanguageWorkerName));

            var finalWebhostChannelCount = await WaitForWebhostWorkerChannelsToStartup(functionDispatcher.ChannelManager, expectedProcessCount, "java");
            Assert.Equal(expectedProcessCount, finalWebhostChannelCount);

            var finalJobhostChannelCount = functionDispatcher.WorkerState.GetChannels().Count();
            Assert.Equal(0, finalJobhostChannelCount);
        }

        [Fact]
        public void MaxProcessCount_Returns_Default()
        {
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher();
            Assert.Equal(1, functionDispatcher.MaxProcessCount);

            functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher("0");
            Assert.Equal(1, functionDispatcher.MaxProcessCount);

            functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher("-1");
            Assert.Equal(1, functionDispatcher.MaxProcessCount);
        }

        [Fact]
        public void MaxProcessCount_ProcessCount_Set_Returns_ExpectedCount()
        {
            int expectedProcessCount = 3;
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher(expectedProcessCount.ToString());
            Assert.Equal(expectedProcessCount, functionDispatcher.MaxProcessCount);
        }

        [Fact]
        public void MaxProcessCount_ProcessCount_Set_ExceedsMax_Returns_ExpectedCount()
        {
            int expectedProcessCount = 30;
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher(expectedProcessCount.ToString());
            Assert.Equal(10, functionDispatcher.MaxProcessCount);
        }

        [Fact]
        public async void FunctionDispatcherState_Default_DotNetFunctions()
        {
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher();
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
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher();
            Assert.Equal(FunctionDispatcherState.Default, functionDispatcher.State);
            await functionDispatcher.InitializeAsync(new List<FunctionMetadata>());
        }

        [Fact]
        public async void ShutdownChannels_NoFunctions()
        {
            var mockLanguageWorkerChannelManager = new Mock<ILanguageWorkerChannelManager>();
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher(mockLanguageWorkerChannelManager: mockLanguageWorkerChannelManager);
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
            var mockLanguageWorkerChannelManager = new Mock<ILanguageWorkerChannelManager>();
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher(mockLanguageWorkerChannelManager: mockLanguageWorkerChannelManager);
            Assert.Equal(FunctionDispatcherState.Default, functionDispatcher.State);
            await functionDispatcher.InitializeAsync(functions);
            // Wait longer than debouce action.
            await Task.Delay(6000);
            mockLanguageWorkerChannelManager.Verify(m => m.ShutdownChannels(), Times.Once);
        }

        [Fact]
        public async void FunctionDispatcherState_Transitions_From_Starting_To_Initialized()
        {
            FunctionDispatcher functionDispatcher = (FunctionDispatcher)GetTestFunctionDispatcher();
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
                TestLanguageWorkerChannel testWorkerChannel = (TestLanguageWorkerChannel)functionDispatcher.WorkerState.GetChannels().FirstOrDefault();
                if (functionDispatcher.WorkerState.Errors.Count < (expectedProcessCount * 3) - 1)
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
                foreach (var channel in functionDispatcher.WorkerState.GetChannels())
                {
                    TestLanguageWorkerChannel testWorkerChannel = channel as TestLanguageWorkerChannel;
                    testWorkerChannel.RaiseWorkerError();
                }
            }
            Assert.Equal(0, functionDispatcher.WorkerState.GetChannels().Count());
        }

        private static IFunctionDispatcher GetTestFunctionDispatcher(string maxProcessCountValue = null, bool addWebhostChannel = false, Mock<ILanguageWorkerChannelManager> mockLanguageWorkerChannelManager = null)
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

            ILanguageWorkerChannelManager testLanguageWorkerChannelManager = new TestLanguageWorkerChannelManager(eventManager, testLogger, scriptOptions.Value.RootScriptPath);
            if (addWebhostChannel)
            {
                testLanguageWorkerChannelManager.InitializeChannelAsync("java");
            }
            var mockFunctionDispatcherLoadBalancer = new Mock<IFunctionDispatcherLoadBalancer>();
            if (mockLanguageWorkerChannelManager != null)
            {
                return new FunctionDispatcher(scriptOptions, metricsLogger.Object, testEnv, scriptJobHostEnvironment.Object, eventManager, loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(workerConfigOptions), mockLanguageWorkerChannelManager.Object, null, mockFunctionDispatcherLoadBalancer.Object);
            }
            return new FunctionDispatcher(scriptOptions, metricsLogger.Object, testEnv, scriptJobHostEnvironment.Object, eventManager, loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(workerConfigOptions), testLanguageWorkerChannelManager, null, mockFunctionDispatcherLoadBalancer.Object);
        }

        private static IFunctionDispatcher GetTestFunctionDispatcherWithMockLanguageWorkerChannelManager(string maxProcessCountValue = null, bool addWebhostChannel = false)
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

            var languageWorkerChannelManager = new Mock<ILanguageWorkerChannelManager>();
            var mockFunctionDispatcherLoadBalancer = new Mock<IFunctionDispatcherLoadBalancer>();
            return new FunctionDispatcher(scriptOptions, metricsLogger.Object, testEnv, scriptJobHostEnvironment.Object, eventManager, loggerFactory, new OptionsWrapper<LanguageWorkerOptions>(workerConfigOptions), languageWorkerChannelManager.Object, null, mockFunctionDispatcherLoadBalancer.Object);
        }

        private async Task<int> WaitForJobhostWorkerChannelsToStartup(IFunctionDispatcher functionDispatcher, int expectedCount)
        {
            int currentChannelCount = 0;
            await TestHelpers.Await(() =>
            {
                currentChannelCount = functionDispatcher.WorkerState.GetChannels().Count();
                return currentChannelCount == expectedCount;
            }, pollingInterval: 5 * 1000, timeout: 60 * 1000);
            return currentChannelCount;
        }

        private async Task<int> WaitForWebhostWorkerChannelsToStartup(ILanguageWorkerChannelManager channelManager, int expectedCount, string language)
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
