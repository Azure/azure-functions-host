// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Tests.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using WebJobs.Script.Tests;
using Xunit;
using Xunit.Abstractions;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public class WorkerConcurrencyManagerTest
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly TestEnvironment _testEnvironment;
        private readonly IOptionsMonitor<FunctionsHostingConfigOptions> _optionsMonitor;
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly ITestOutputHelper _output;

        public WorkerConcurrencyManagerTest(ITestOutputHelper output)
        {
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_loggerProvider);
            _testEnvironment = new TestEnvironment();
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled, "true");
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.PythonLanguageWorkerName);
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, RpcWorkerConstants.NodeLanguageWorkerName);

            var optionsMonitor = new Mock<IOptionsMonitor<FunctionsHostingConfigOptions>>();
            optionsMonitor.Setup(x => x.CurrentValue).Returns(new FunctionsHostingConfigOptions());
            _optionsMonitor = optionsMonitor.Object;

            Mock<IApplicationLifetime> applicationLifetime = new Mock<IApplicationLifetime>();
            applicationLifetime.Setup(x => x.StopApplication()).Verifiable();
            _applicationLifetime = applicationLifetime.Object;

            _output = output;
        }

        public static IEnumerable<object[]> DataForIsOverloaded =>
            new List<object[]>
            {
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        HistorySize = 5,
                    },
                    new int[] { 1, 2, 3, 4 },
                    false
                },
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        LatencyThreshold = TimeSpan.FromMilliseconds(10),
                        HistorySize = 5
                    },
                    new int[] { 1, 2, 3, 4, 5 },
                    false
                },
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        LatencyThreshold = TimeSpan.FromMilliseconds(10),
                        HistorySize = 5
                    },
                    new int[] { 11, 12, 13, 14, 15 },
                    true
                },
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        LatencyThreshold = TimeSpan.FromMilliseconds(13),
                        HistorySize = 6,
                        NewWorkerThreshold = 0.5F
                    },
                    new int[] { 11, 12, 13, 14, 15, 16 },
                    true
                },
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        LatencyThreshold = TimeSpan.FromMilliseconds(15),
                        HistorySize = 6,
                        NewWorkerThreshold = 0.5F
                    },
                    new int[] { 11, 12, 13, 14, 15, 16 },
                    false
                },
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        LatencyThreshold = TimeSpan.FromMilliseconds(14),
                        HistorySize = 5,
                    },
                    new int[] { },
                    false
                }
            };

        public static IEnumerable<object[]> DataForForAddWorkerIfNeeded =>
            new List<object[]>
            {
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        HistorySize = 5,
                        LatencyThreshold = TimeSpan.FromMilliseconds(200),
                        AdjustmentPeriod = TimeSpan.FromMilliseconds(1000),
                        MaxWorkerCount = 3,
                    },
                    new int[] { 100, 100, 100, 100, 100 },
                    new int[] { 150, 150, 150, 150, 150 },
                    true,
                    true,
                    TimeSpan.FromSeconds(2000),
                    false
                },
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        HistorySize = 5,
                        LatencyThreshold = TimeSpan.FromMilliseconds(110),
                        AdjustmentPeriod = TimeSpan.FromMilliseconds(1000),
                        MaxWorkerCount = 3,
                    },
                    new int[] { 100, 100, 100, 100, 100 },
                    new int[] { 150, 150, 150, 150, 150 },
                    true,
                    true,
                    TimeSpan.FromSeconds(2000),
                    true
                },
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        HistorySize = 5,
                        LatencyThreshold = TimeSpan.FromMilliseconds(110),
                        AdjustmentPeriod = TimeSpan.FromMilliseconds(1000),
                        MaxWorkerCount = 3,
                    },
                    new int[] { 100, 100, 100, 100, 100 },
                    new int[] { 150, 150, 150, 150, 150 },
                    true,
                    false,
                    TimeSpan.FromSeconds(2000),
                    false
                },
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        HistorySize = 5,
                        LatencyThreshold = TimeSpan.FromMilliseconds(110),
                        AdjustmentPeriod = TimeSpan.FromMilliseconds(1000),
                        MaxWorkerCount = 3
                    },
                    new int[] { 100, 100, 100 },
                    new int[] { 150, 150, 150 },
                    true,
                    false,
                    TimeSpan.FromSeconds(2000),
                    false
                },
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        HistorySize = 5,
                        LatencyThreshold = TimeSpan.FromMilliseconds(110),
                        AdjustmentPeriod = TimeSpan.FromMilliseconds(1000),
                        MaxWorkerCount = 3
                    },
                    new int[] { 100, 100, 100, 100, 100 },
                    new int[] { 150, 150, 150, 150, 150 },
                    true,
                    false,
                    TimeSpan.FromSeconds(500),
                    false
                },
                new object[]
                {
                    new WorkerConcurrencyOptions()
                    {
                        HistorySize = 5,
                        LatencyThreshold = TimeSpan.FromMilliseconds(110),
                        AdjustmentPeriod = TimeSpan.FromMilliseconds(1000),
                        MaxWorkerCount = 2,
                    },
                    new int[] { 100, 100, 100, 100, 100 },
                    new int[] { 150, 150, 150, 150, 150 },
                    true,
                    false,
                    TimeSpan.FromSeconds(2000),
                    false
                }
            };

        [Fact]
        public async Task Start_StartsTimer_WhenDynamicConcurrencyEnabled()
        {
            WorkerConcurrencyOptions options = new WorkerConcurrencyOptions()
            {
                AdjustmentPeriod = TimeSpan.Zero
            };

            Mock<IFunctionInvocationDispatcher> functionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>(MockBehavior.Strict);
            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            functionInvocationDispatcherFactory.Setup(x => x.GetFunctionDispatcher()).Returns(functionInvocationDispatcher.Object);
            WorkerConcurrencyManager concurrancyManger = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, _testEnvironment, Options.Create(options),
                _optionsMonitor, _applicationLifetime, _loggerFactory);
            await concurrancyManger.StartAsync(CancellationToken.None);

            await TestHelpers.Await(() =>
            {
                var sratedLog = _loggerProvider.GetAllLogMessages().FirstOrDefault(x => x.FormattedMessage.StartsWith("Starting dynamic worker concurrency monitoring."));
                return sratedLog != null;
            }, pollingInterval: 1000, timeout: 10 * 1000);
        }

        [Fact]
        public async Task Start_DoesNot_StartTimer_WhenDynamicConcurrencyDisabled()
        {
            Mock<IFunctionInvocationDispatcher> functionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>(MockBehavior.Strict);
            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            functionInvocationDispatcherFactory.Setup(x => x.GetFunctionDispatcher()).Returns(functionInvocationDispatcher.Object);
            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled, "false");
            WorkerConcurrencyManager concurrancyManger = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, _testEnvironment, Options.Create(new WorkerConcurrencyOptions()),
                _optionsMonitor, _applicationLifetime, _loggerFactory);
            await concurrancyManger.StartAsync(CancellationToken.None);

            await Task.Delay(1000);
            var sratedLog = _loggerProvider.GetAllLogMessages().FirstOrDefault(x => x.FormattedMessage.StartsWith("Starting dynamic worker concurrency monitoring."));
            Assert.True(sratedLog == null);

            _testEnvironment.SetEnvironmentVariable(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled, null);
        }

        [Fact]
        public async Task Start_HttpWorker_DoesNot_StartTimer()
        {
            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            functionInvocationDispatcherFactory.Setup(x => x.GetFunctionDispatcher()).Returns(new HttpFunctionInvocationDispatcher());
            WorkerConcurrencyManager concurrancyManger = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, _testEnvironment, Options.Create(new WorkerConcurrencyOptions()),
                _optionsMonitor, _applicationLifetime, _loggerFactory);
            await concurrancyManger.StartAsync(CancellationToken.None);

            await Task.Delay(1000);
            var sratedLog = _loggerProvider.GetAllLogMessages().FirstOrDefault(x => x.FormattedMessage.StartsWith("Http dynamic worker concurrency is not supported."));
            Assert.True(sratedLog != null);
        }

        [Theory]
        [MemberData(nameof(DataForIsOverloaded))]
        public async Task IsOverloaded_Returns_Expected(WorkerConcurrencyOptions options, int[] latencies, bool expected)
        {
            Mock<IFunctionInvocationDispatcher> functionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>(MockBehavior.Strict);
            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            functionInvocationDispatcherFactory.Setup(x => x.GetFunctionDispatcher()).Returns(functionInvocationDispatcher.Object);
            WorkerConcurrencyManager concurrancyManger = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, _testEnvironment, Options.Create(options),
                _optionsMonitor, _applicationLifetime, _loggerFactory);
            await concurrancyManger.StartAsync(CancellationToken.None);

            WorkerStatus status = new WorkerStatus()
            {
                LatencyHistory = latencies.Select(x => TimeSpan.FromMilliseconds(x))
            };

            Assert.Equal(concurrancyManger.IsOverloaded(status), expected);
        }

        [Theory]
        [MemberData(nameof(DataForForAddWorkerIfNeeded))]
        public async Task AddWorkerIfNeeded_Returns_Expected(WorkerConcurrencyOptions options,
            int[] latencies1, int[] latencies2, bool readyForInvocations1, bool readyForInvocations2,
            TimeSpan elapsedFromLastAdding, bool expected)
        {
            Mock<IFunctionInvocationDispatcher> functionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>(MockBehavior.Strict);
            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            functionInvocationDispatcherFactory.Setup(x => x.GetFunctionDispatcher()).Returns(functionInvocationDispatcher.Object);

            Dictionary<string, WorkerStatus> workerStatuses = new Dictionary<string, WorkerStatus>();

            workerStatuses.Add("test1", new WorkerStatus()
            {
                IsReady = readyForInvocations1,
                LatencyHistory = latencies1.Select(x => TimeSpan.FromMilliseconds(x))
            });
            workerStatuses.Add("test2", new WorkerStatus()
            {
                IsReady = readyForInvocations2,
                LatencyHistory = latencies2.Select(x => TimeSpan.FromMilliseconds(x))
            });

            WorkerConcurrencyManager concurrencyManager = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, _testEnvironment, Options.Create(options),
                _optionsMonitor, _applicationLifetime, _loggerFactory);
            await concurrencyManager.StartAsync(CancellationToken.None);
            bool value = concurrencyManager.NewWorkerIsRequired(workerStatuses, elapsedFromLastAdding);

            Assert.Equal(value, expected);
        }

        [Theory]
        [InlineData(RpcWorkerConstants.JavaLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.DotNetLanguageWorkerName)]
        public async Task StartAsync_DoesNotGetDispatcher(string workerRuntime)
        {
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, workerRuntime);
            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            WorkerConcurrencyManager concurrencyManager = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, _testEnvironment, Options.Create(new WorkerConcurrencyOptions()),
                _optionsMonitor, _applicationLifetime, _loggerFactory);
            await concurrencyManager.StartAsync(CancellationToken.None);
        }

        [Theory]
        [InlineData(RpcWorkerConstants.NodeLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.PowerShellLanguageWorkerName)]
        [InlineData(RpcWorkerConstants.PythonLanguageWorkerName)]
        public async Task StartAsync_GetsDispatcher(string workerRuntime)
        {
            _testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, workerRuntime);
            Mock<IFunctionInvocationDispatcher> functionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>(MockBehavior.Strict);
            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            functionInvocationDispatcherFactory.Setup(x => x.GetFunctionDispatcher()).Returns(functionInvocationDispatcher.Object);

            WorkerConcurrencyManager concurrencyManager = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, _testEnvironment, Options.Create(new WorkerConcurrencyOptions()),
                _optionsMonitor, _applicationLifetime, _loggerFactory);
            await concurrencyManager.StartAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ActivateWorkerConcurency_FunctionsHostingConfiguration_WorkAsExpected()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                WorkerConcurrencyManager manager = null;
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                IHost host = FunctionsHostingConfigOptionsTest.GetScriptHostBuilder(fileName, $"feature1=value1,{RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled}=1")
                    .ConfigureServices((context, services) =>
                    {
                        services.AddSingleton<IHostedService, WorkerConcurrencyManager>(serviceProvider =>
                        {
                            var monitor = serviceProvider.GetService<IOptionsMonitor<FunctionsHostingConfigOptions>>();

                            var workerConcurrencyOptions = Options.Create(new WorkerConcurrencyOptions());
                            Mock<IFunctionInvocationDispatcher> functionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>();
                            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
                            functionInvocationDispatcherFactory.Setup(x => x.GetFunctionDispatcher()).Returns(functionInvocationDispatcher.Object);
                            TestEnvironment testEnvironment = new TestEnvironment();
                            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, RpcWorkerConstants.NodeLanguageWorkerName);

                            manager = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, testEnvironment, workerConcurrencyOptions,
                                monitor, _applicationLifetime, _loggerFactory);

                            return manager;
                        });
                    }).Build();

                await host.StartAsync();

                await TestHelpers.Await(() => _loggerProvider.GetAllLogMessages()
                    .SingleOrDefault(x => x.FormattedMessage.StartsWith("Dynamic worker concurrency monitoring is starting by hosting config.")) != null, timeout: 10000, pollingInterval: 100);
                File.WriteAllText(fileName, "feature1=value1");
                await TestHelpers.Await(() => _loggerProvider.GetAllLogMessages()
                    .SingleOrDefault(x => x.FormattedMessage.StartsWith("Dynamic worker concurrency monitoring is stopping on hosting config update. Shutting down Functions Host.")) != null, timeout: 10000, pollingInterval: 100);
            }
        }

        [Theory]
        [InlineData(100, 20, new long[] { 20, 20 }, true)]
        [InlineData(100, 20, new long[] { 20, 10, 10 }, true)]
        [InlineData(100, 21, new long[] { 20, 10, 10 }, false)]
        public void IsEnoughMemory_WorkAsExpected(long availableMemory, long hostProcessSize, IEnumerable<long> languageWorkerSizes, bool result)
        {
            TestEnvironment testEnvironment = new TestEnvironment();
            Mock<IFunctionInvocationDispatcher> functionInvocationDispatcher = new Mock<IFunctionInvocationDispatcher>(MockBehavior.Strict);
            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            WorkerConcurrencyOptions options = new WorkerConcurrencyOptions();

            WorkerConcurrencyManager concurrencyManager = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, testEnvironment,
                Options.Create(options), _optionsMonitor, _applicationLifetime, _loggerFactory);

            Assert.True(concurrencyManager.IsEnoughMemoryToScale(hostProcessSize, languageWorkerSizes, availableMemory) == result);
            if (!result)
            {
                Assert.Contains(_loggerProvider.GetAllLogMessages().Select(x => x.FormattedMessage), x => x.StartsWith("Starting new language worker canceled:"));
            }
        }

        [Theory]
        [InlineData(4, new bool[] { true, true, true }, true)]
        [InlineData(3, new bool[] { true, true, true }, false)]
        [InlineData(4, new bool[] { true, false, true }, false)]
        public void CanScale_ReturnsExpected(int maxWorkerCount, bool[] isReadyArray, bool result)
        {
            TestEnvironment testEnvironment = new TestEnvironment();
            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            WorkerConcurrencyOptions options = new WorkerConcurrencyOptions();
            options.MaxWorkerCount = maxWorkerCount;

            WorkerConcurrencyManager concurrencyManager = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, testEnvironment,
                Options.Create(options), _optionsMonitor, _applicationLifetime, _loggerFactory);

            List<IRpcWorkerChannel> workerChannels = new List<IRpcWorkerChannel>();
            foreach (bool isReady in isReadyArray)
            {
                Mock<IRpcWorkerChannel> mock = new Mock<IRpcWorkerChannel>();
                mock.Setup(x => x.IsChannelReadyForInvocations()).Returns(isReady);
                workerChannels.Add(mock.Object);
            }
            Assert.Equal(concurrencyManager.CanScale(workerChannels), result);
        }

        [Fact]
        public void OnTimer_WorksAsExpected_IfPlaceholderMode_Enabled()
        {
            TestEnvironment testEnvironment = new TestEnvironment();
            Mock<IFunctionInvocationDispatcherFactory> functionInvocationDispatcherFactory = new Mock<IFunctionInvocationDispatcherFactory>(MockBehavior.Strict);
            WorkerConcurrencyOptions options = new WorkerConcurrencyOptions();

            WorkerConcurrencyManager concurrencyManager = new WorkerConcurrencyManager(functionInvocationDispatcherFactory.Object, testEnvironment,
                Options.Create(options), _optionsMonitor, _applicationLifetime, _loggerFactory);

            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            concurrencyManager.OnTimer(null, null);
            Assert.Empty(_loggerProvider.GetAllLogMessages().Where(x => x.FormattedMessage == "Error monitoring worker concurrency"));
        }
    }
}
