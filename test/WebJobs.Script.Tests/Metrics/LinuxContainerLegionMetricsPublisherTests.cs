// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Platform.Metrics.LinuxConsumption;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Metrics
{
    [Trait(TestTraits.Group, TestTraits.LinuxConsumptionMetricsTests)]
    public class LinuxContainerLegionMetricsPublisherTests
    {
        private const string TestFunctionName = "testfunction";

        private readonly string _metricsFilePath;
        private readonly IEnvironment _environment;
        private readonly TestMetricsTracker _testMetricsTracker;
        private readonly Random _random = new Random();
        private readonly TestLogger<LinuxContainerLegionMetricsPublisher> _logger;
        private readonly TestMetricsLogger _testMetricsLogger;
        private readonly IServiceProvider _serviceProvider;

        private IOptions<LinuxConsumptionLegionMetricsPublisherOptions> _options;
        private StandbyOptions _standbyOptions;
        private TestOptionsMonitor<StandbyOptions> _standbyOptionsMonitor;

        public LinuxContainerLegionMetricsPublisherTests()
        {
            _metricsFilePath = Path.Combine(Path.GetTempPath(), "metrics");
            _environment = new TestEnvironment();
            _logger = new TestLogger<LinuxContainerLegionMetricsPublisher>();
            _testMetricsLogger = new TestMetricsLogger();
            _serviceProvider = new TestScriptHostManager(_testMetricsLogger);

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsMetricsPublishPath, _metricsFilePath);

            CleanupMetricsFiles();
            _testMetricsTracker = new TestMetricsTracker();
        }

        private LinuxContainerLegionMetricsPublisher CreatePublisher(int? metricsPublishInterval = null, bool inStandbyMode = false)
        {
            _standbyOptions = new StandbyOptions { InStandbyMode = inStandbyMode };
            _standbyOptionsMonitor = new TestOptionsMonitor<StandbyOptions>(_standbyOptions);
            _options = Options.Create(new LinuxConsumptionLegionMetricsPublisherOptions
            {
                ContainerName = "testcontainer",
                MetricsFilePath = _metricsFilePath
            });

            var testHostingConfigOptionsMonitor = new TestOptionsMonitor<FunctionsHostingConfigOptions>(new FunctionsHostingConfigOptions());

            return new LinuxContainerLegionMetricsPublisher(_environment, _standbyOptionsMonitor, _options, _logger, new FileSystem(), _testMetricsTracker, _serviceProvider, testHostingConfigOptionsMonitor, metricsPublishInterval);
        }

        [Fact]
        public async Task AddActivities_ExpectedActivitiesArePublished()
        {
            var metricsPublisher = CreatePublisher(inStandbyMode: true);
            metricsPublisher.Initialize();

            await AddTestActivities(metricsPublisher, 10, 25);

            Assert.Equal(10, _testMetricsTracker.FunctionActivities.Count);
            var functionActivity = _testMetricsTracker.FunctionActivities[0];
            Assert.Equal(TestFunctionName, functionActivity.FunctionName);
            Assert.Equal(FunctionExecutionStage.Finished, functionActivity.ExecutionStage);

            Assert.Equal(25, _testMetricsTracker.MemoryActivities.Count);
            var memoryActivity = _testMetricsTracker.MemoryActivities[0];
        }

        [Fact]
        public async Task AddActivities_StandbyMode_ActivitiesNotPublished()
        {
            var metricsPublisher = CreatePublisher(inStandbyMode: true);

            await AddTestActivities(metricsPublisher, 10, 25);

            Assert.Equal(0, _testMetricsTracker.FunctionActivities.Count);
            Assert.Equal(0, _testMetricsTracker.MemoryActivities.Count);
        }

        [Fact]
        public async Task PublishMetrics_WritesExpectedFile()
        {
            var newMetrics = new LinuxConsumptionMetrics
            {
                FunctionExecutionCount = 111,
                FunctionExecutionTimeMS = 222,
                FunctionActivity = 333333
            };
            _testMetricsTracker.MetricsQueue.Enqueue(newMetrics);

            var metricsPublisher = CreatePublisher(inStandbyMode: true);

            await metricsPublisher.OnPublishMetricsAsync();

            FileInfo[] metricsFiles = GetMetricsFilesSafe(_metricsFilePath);
            Assert.Equal(1, metricsFiles.Length);

            var metrics = await ReadMetricsAsync(metricsFiles[0].FullName);

            Assert.Equal(newMetrics.FunctionExecutionCount, metrics.ExecutionCount);
            Assert.Equal(newMetrics.FunctionExecutionTimeMS, metrics.ExecutionTimeMS);
            Assert.Equal(newMetrics.FunctionActivity, metrics.FunctionActivity);
        }

        [Fact]
        public async Task MetricsFilesPublishedOnInterval()
        {
            EnqueueTestMetrics(5);

            var metricsPublisher = CreatePublisher(metricsPublishInterval: 100);

            FileInfo[] metricsFiles = null;
            await TestHelpers.Await(() =>
            {
                metricsFiles = GetMetricsFilesSafe(_metricsFilePath);
                return metricsFiles.Length == 5;
            });

            Assert.Equal(5, metricsFiles.Length);
        }

        [Fact]
        public async Task MetricsFilesNotPublished_WhenMetricsNotAvailable()
        {
            var metricsPublisher = CreatePublisher(metricsPublishInterval: 100);

            await Task.Delay(500);

            FileInfo[] metricsFiles = GetMetricsFilesSafe(_metricsFilePath);
            Assert.Empty(metricsFiles);
        }

        [Fact]
        public void LogEvent_LogsMetricEvent()
        {
            var metricsPublisher = CreatePublisher();

            for (int i = 0; i < 10; i++)
            {
                _testMetricsTracker.LogEvent("testevent1");
            }

            for (int i = 0; i < 5; i++)
            {
                _testMetricsTracker.LogEvent("testevent2");
            }

            Assert.Equal(15, _testMetricsLogger.LoggedEvents.Count);
            Assert.Equal(10, _testMetricsLogger.LoggedEvents.Count(p => p == "testevent1"));
            Assert.Equal(5, _testMetricsLogger.LoggedEvents.Count(p => p == "testevent2"));
        }

        private void EnqueueTestMetrics(int numMetrics)
        {
            for (int i = 0; i < numMetrics; i++)
            {
                _testMetricsTracker.MetricsQueue.Enqueue(new LinuxConsumptionMetrics
                {
                    FunctionExecutionCount = _random.Next(1, 100),
                    FunctionExecutionTimeMS = _random.Next(1, 100),
                    FunctionActivity = _random.Next(10000, 1000000)
                });
            }
        }

        private async Task AddTestActivities(LinuxContainerLegionMetricsPublisher metricsPublisher, int numFunctionActivities, int numMemoryActivities)
        {
            var t1 = Task.Run(async () =>
            {
                for (int i = 0; i < numFunctionActivities; i++)
                {
                    var startTime = DateTime.UtcNow;
                    await Task.Delay(25);
                    var endTime = DateTime.UtcNow;
                    var duration = endTime - startTime;
                    metricsPublisher.AddFunctionExecutionActivity(TestFunctionName, Guid.NewGuid().ToString(), 50, FunctionExecutionStage.Finished.ToString(), true, (long)duration.TotalMilliseconds, Guid.NewGuid().ToString(), DateTime.UtcNow, startTime);
                }
            });

            var t2 = Task.Run(async () =>
            {
                for (int i = 0; i < numMemoryActivities; i++)
                {
                    await Task.Delay(10);
                    metricsPublisher.AddMemoryActivity(DateTime.UtcNow, 1000);
                }
            });

            await Task.WhenAll(t1, t2);
        }

        public void CleanupMetricsFiles()
        {
            var directory = new DirectoryInfo(_metricsFilePath);

            if (!directory.Exists)
            {
                return;
            }

            foreach (var file in directory.GetFiles())
            {
                file.Delete();
            }
        }

        private static async Task<LinuxContainerLegionMetricsPublisher.Metrics> ReadMetricsAsync(string metricsFilePath)
        {
            string content = await File.ReadAllTextAsync(metricsFilePath);
            return JsonConvert.DeserializeObject<LinuxContainerLegionMetricsPublisher.Metrics>(content);
        }

        private static FileInfo[] GetMetricsFilesSafe(string path)
        {
            var directory = new DirectoryInfo(path);
            if (directory.Exists)
            {
                return directory.GetFiles().OrderBy(p => p.CreationTime).ToArray();
            }

            return new FileInfo[0];
        }

        private class TestScriptHostManager : IServiceProvider, IScriptHostManager
        {
            private readonly IMetricsLogger _metricsLogger;

            public TestScriptHostManager(IMetricsLogger metricsLogger)
            {
                _metricsLogger = metricsLogger;
            }

#pragma warning disable CS0067
            public event EventHandler HostInitializing;

            public event EventHandler<ActiveHostChangedEventArgs> ActiveHostChanged;
#pragma warning restore CS0067

            public ScriptHostState State => throw new NotImplementedException();

            public Exception LastError => throw new NotImplementedException();

            public object GetService(Type serviceType)
            {
                if (serviceType == typeof(IMetricsLogger))
                {
                    return _metricsLogger;
                }

                throw new NotImplementedException();
            }

            public Task RestartHostAsync(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }
    }
}