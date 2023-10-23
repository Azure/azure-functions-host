// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Metrics
{
    [Trait(TestTraits.Group, TestTraits.FlexConsumptionMetricsTests)]
    public class FlexConsumptionMetricsPublisherTests
    {
        private string _metricsFilePath;
        private IEnvironment _environment;
        private StandbyOptions _standbyOptions;
        private TestOptionsMonitor<StandbyOptions> _standbyOptionsMonitor;
        private FlexConsumptionMetricsPublisherOptions _options;
        private TestLogger<FlexConsumptionMetricsPublisher> _logger;

        public FlexConsumptionMetricsPublisherTests()
        {
            _metricsFilePath = Path.Combine(Path.GetTempPath(), "metrics");
            _environment = new TestEnvironment();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PublisherStartsOnSpecialization(bool publishEnabled)
        {
            if (!publishEnabled)
            {
                _metricsFilePath = null;
            }

            var publisher = CreatePublisher(inStandbyMode: true);

            var logs = _logger.GetLogMessages();
            var log = logs.Single();
            Assert.Equal(LogLevel.Debug, log.Level);
            Assert.Equal("Registering StandbyOptions change subscription.", log.FormattedMessage);

            _standbyOptions.InStandbyMode = false;
            _standbyOptionsMonitor.InvokeChanged();

            logs = _logger.GetLogMessages();
            log = logs.Single(p => p.FormattedMessage == $"Starting metrics publisher (AlwaysReady={false}, MetricsPath='{_metricsFilePath}').");
            Assert.NotNull(log);
        }

        private FlexConsumptionMetricsPublisher CreatePublisher(TimeSpan? metricsPublishInterval = null, bool inStandbyMode = false)
        {
            _standbyOptions = new StandbyOptions { InStandbyMode = inStandbyMode };
            _standbyOptionsMonitor = new TestOptionsMonitor<StandbyOptions>(_standbyOptions);
            _options = new FlexConsumptionMetricsPublisherOptions
            {
                // for these unit tests we don't want the publish timer running -
                // we want to publish manually
                InitialPublishDelayMS = Timeout.Infinite,
                MetricsFilePath = _metricsFilePath,
                MaxFileCount = 5
            };
            if (metricsPublishInterval != null)
            {
                _options.MetricsPublishIntervalMS = (int)metricsPublishInterval.Value.TotalMilliseconds;
            }
            var optionsWrapper = new OptionsWrapper<FlexConsumptionMetricsPublisherOptions>(_options);
            _logger = new TestLogger<FlexConsumptionMetricsPublisher>();
            var publisher = new FlexConsumptionMetricsPublisher(_environment, _standbyOptionsMonitor, optionsWrapper, _logger, new FileSystem());

            return publisher;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task OnPublishMetrics_WritesFileAndResetsCounts(bool isAlwaysReadyInstance)
        {
            CleanupMetricsFiles();

            if (isAlwaysReadyInstance)
            {
                _environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsAlwaysReadyInstance, "1");
            }

            var publisher = CreatePublisher();

            int delay = 100;
            await Task.Delay(delay);

            await publisher.OnPublishMetrics();

            FileInfo[] files = GetMetricsFilesSafe(_metricsFilePath);

            FlexConsumptionMetricsPublisher.Metrics metrics = null;
            FileInfo metricsFile = null;
            if (!isAlwaysReadyInstance)
            {
                // don't expect a file to be written when no activity
                Assert.Equal(0, files.Length);
            }
            else
            {
                Assert.Equal(1, files.Length);

                metricsFile = files[0];
                metrics = await ReadMetricsAsync(metricsFile.FullName);
                Assert.True(metrics.IsAlwaysReady);
                ValidateTotalTime(metrics.TotalTimeMS, delay);
                Assert.Equal(0, metrics.ExecutionCount);
                Assert.Equal(0, metrics.ExecutionTimeMS);

                metricsFile.Delete();
            }

            int executionDurationMS = 5678;
            publisher.FunctionExecutionCount = 123;
            publisher.FunctionExecutionTimeMS = executionDurationMS;

            delay = (int)executionDurationMS + 100;
            await Task.Delay(delay);

            await publisher.OnPublishMetrics();

            files = GetMetricsFilesSafe(_metricsFilePath);
            Assert.Equal(1, files.Length);

            metricsFile = files[0];
            metrics = await ReadMetricsAsync(metricsFile.FullName);
            Assert.Equal(123, metrics.ExecutionCount);
            Assert.Equal(5678, metrics.ExecutionTimeMS);
            ValidateTotalTime(metrics.TotalTimeMS, delay);
            Assert.Equal(isAlwaysReadyInstance, metrics.IsAlwaysReady);

            Assert.Equal(0, publisher.FunctionExecutionCount);
            Assert.Equal(0, publisher.FunctionExecutionTimeMS);
        }

        [Fact]
        public async Task OnPublishMetrics_PurgesOldFiles()
        {
            CleanupMetricsFiles();

            List<string> metricsFiles = new List<string>();
            var fileSystem = new FileSystem();
            for (int i = 0; i < 10; i++)
            {
                string fileName = $"{Guid.NewGuid().ToString().ToLower()}.json";
                metricsFiles.Add(fileName);
                string filePath = Path.Combine(_metricsFilePath, fileName);
                var sw = fileSystem.File.CreateText(filePath);
                sw.Close();

                await Task.Delay(50);
            }

            var publisher = CreatePublisher();

            int executionDurationMS = 5678;
            publisher.FunctionExecutionCount = 123;
            publisher.FunctionExecutionTimeMS = executionDurationMS;

            int delay = (int)executionDurationMS + 100;
            await Task.Delay(delay);

            await publisher.OnPublishMetrics();

            FileInfo[] files = GetMetricsFilesSafe(_metricsFilePath);
            Assert.Equal(5, files.Length);

            // we expect the oldest 4 of the initial files to be retained,
            // along with the last file written
            var expectedfiles = metricsFiles.Skip(6).Take(4).ToArray();

            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(expectedfiles[i], files[i].Name);
            }

            var logs = _logger.GetLogMessages().ToArray();
            Assert.Equal(3, logs.Length);

            var log = logs[0];
            Assert.Equal(LogLevel.Information, log.Level);
            Assert.Equal($"Starting metrics publisher (AlwaysReady={false}, MetricsPath='{_metricsFilePath}').", log.FormattedMessage);

            log = logs[1];
            Assert.Equal(LogLevel.Debug, log.Level);
            Assert.Equal("Deleting 6 metrics file(s).", log.FormattedMessage);

            log = logs[2];
            Assert.Equal(LogLevel.Debug, log.Level);
            Assert.Equal("PublishingMetrics", log.EventId.Name);
            var metrics = JsonConvert.DeserializeObject<FlexConsumptionMetricsPublisher.Metrics>(log.FormattedMessage);
            ValidateTotalTime(metrics.TotalTimeMS, delay);
            Assert.Equal(5678, metrics.ExecutionTimeMS);
            Assert.Equal(123, metrics.ExecutionCount);
            Assert.False(metrics.IsAlwaysReady);

            log = logs[2];
        }

        [Fact]
        public void FunctionsStartStop_CorrectCountsAreMaintained()
        {
            var publisher = CreatePublisher(metricsPublishInterval: TimeSpan.FromHours(1), inStandbyMode: false);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(0, publisher.FunctionExecutionCount);
            Assert.Equal(0, publisher.FunctionExecutionTimeMS);

            publisher.OnFunctionStarted("foo", "111");

            Assert.Equal(1, publisher.ActiveFunctionCount);
            Assert.Equal(0, publisher.FunctionExecutionCount);
            Assert.Equal(0, publisher.FunctionExecutionTimeMS);

            publisher.OnFunctionStarted("bar", "222");
            publisher.OnFunctionStarted("baz", "333");

            Assert.Equal(3, publisher.ActiveFunctionCount);
            Assert.Equal(0, publisher.FunctionExecutionCount);
            Assert.Equal(0, publisher.FunctionExecutionTimeMS);

            publisher.OnFunctionCompleted("foo", "111");
            publisher.OnFunctionCompleted("bar", "222");

            Assert.Equal(1, publisher.ActiveFunctionCount);
            Assert.Equal(2, publisher.FunctionExecutionCount);
            Assert.Equal(0, publisher.FunctionExecutionTimeMS);

            publisher.OnFunctionCompleted("foo", "111");

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(3, publisher.FunctionExecutionCount);
            Assert.True(publisher.FunctionExecutionTimeMS > 0);
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

        private static async Task<FlexConsumptionMetricsPublisher.Metrics> ReadMetricsAsync(string metricsFilePath)
        {
            string content = await File.ReadAllTextAsync(metricsFilePath);
            return JsonConvert.DeserializeObject<FlexConsumptionMetricsPublisher.Metrics>(content);
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

        private static void ValidateTotalTime(long value, long upperBound)
        {
            // Ensure the measured total time for the timer interval is within
            // the expected range (plus a small margin of error)
            // For these unit tests, the timer isn't actually running - we're
            // initiating the publish operations manually so we control the interval.
            Assert.InRange(value, 0, upperBound + 50);
        }
    }
}
