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
using Moq;
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
        private HostMetricsProvider _metricsProvider;

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
            var serviceProvider = new Mock<IServiceProvider>();
            var hostMetricsLogger = new TestLogger<HostMetricsProvider>();
            _metricsProvider = new HostMetricsProvider(serviceProvider.Object, _standbyOptionsMonitor, hostMetricsLogger, _environment);
            var publisher = new FlexConsumptionMetricsPublisher(_environment, _standbyOptionsMonitor, optionsWrapper, _logger, new FileSystem(), _metricsProvider);

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

            int executionDurationMS = 5700;
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
            Assert.Equal(5700, metrics.ExecutionTimeMS);
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

            publisher.OnFunctionCompleted("baz", "333");

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(3, publisher.FunctionExecutionCount);
            Assert.True(publisher.FunctionExecutionTimeMS > 0);
        }

        [Fact]
        public void FunctionsStartStop_MinimumActivityIntervals_Scenario1()
        {
            var publisher = CreatePublisher(metricsPublishInterval: TimeSpan.FromHours(1), inStandbyMode: false);

            Assert.Equal(1000, _options.MinimumActivityIntervalMS);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(0, publisher.FunctionExecutionCount);
            Assert.Equal(0, publisher.FunctionExecutionTimeMS);

            DateTime now = DateTime.UtcNow;

            // function starts and completes with a duration less than the minimum
            publisher.OnFunctionStarted("foo", "1", now);
            now += TimeSpan.FromMilliseconds(350);
            publisher.OnFunctionCompleted("foo", "1", now);

            // we're metered for the minimum interval with no rounding
            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(1, publisher.FunctionExecutionCount);
            Assert.Equal(1000, publisher.FunctionExecutionTimeMS);

            // now 100ms later we start a new invocation that runs for 200ms
            // we don't expect to be metered for this since it's still within
            // the above 1s window
            now += TimeSpan.FromMilliseconds(100);
            publisher.OnFunctionStarted("foo", "2", now);
            now += TimeSpan.FromMilliseconds(200);
            publisher.OnFunctionCompleted("foo", "2", now);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(2, publisher.FunctionExecutionCount);
            Assert.Equal(1000, publisher.FunctionExecutionTimeMS);

            // after 30ms another invocation starts and runs for 1200ms
            // this finally takes us out of the previous 1s window
            // 320ms of the 1200ms comes from the previous window,
            // leaving 880ms. This is again rounded up to 1s and a
            // new window runs out 120ms from now
            now += TimeSpan.FromMilliseconds(30);
            publisher.OnFunctionStarted("foo", "3", now);
            now += TimeSpan.FromMilliseconds(1200);
            publisher.OnFunctionCompleted("foo", "31", now);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(3, publisher.FunctionExecutionCount);
            Assert.Equal(2000, publisher.FunctionExecutionTimeMS);

            // after 300ms another invocation starts and runs for 1320ms
            // because we're out of the previous window, we're metered
            // for this duration and rounded up to 1400ms
            now += TimeSpan.FromMilliseconds(300);
            publisher.OnFunctionStarted("foo", "4", now);
            now += TimeSpan.FromMilliseconds(1320);
            publisher.OnFunctionCompleted("foo", "4", now);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(4, publisher.FunctionExecutionCount);
            Assert.Equal(3400, publisher.FunctionExecutionTimeMS);

            // finally, after a short delay of 50ms, we have 2 invocations that
            // start, overlap, then complete in an activity duration of 3.5s
            now += TimeSpan.FromMilliseconds(50);
            publisher.OnFunctionStarted("foo", "5", now);
            now += TimeSpan.FromMilliseconds(250);
            publisher.OnFunctionStarted("bar", "6", now);
            now += TimeSpan.FromMilliseconds(750);
            publisher.OnFunctionCompleted("foo", "5", now);
            now += TimeSpan.FromMilliseconds(2500);
            publisher.OnFunctionCompleted("bar", "6", now);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(6, publisher.FunctionExecutionCount);
            Assert.Equal(6900, publisher.FunctionExecutionTimeMS);
        }

        [Fact]
        public void FunctionsStartStop_MinimumActivityIntervals_Scenario2()
        {
            var publisher = CreatePublisher(metricsPublishInterval: TimeSpan.FromHours(1), inStandbyMode: false);

            Assert.Equal(1000, _options.MinimumActivityIntervalMS);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(0, publisher.FunctionExecutionCount);
            Assert.Equal(0, publisher.FunctionExecutionTimeMS);

            DateTime now = DateTime.UtcNow;

            // Run 10 100ms invocations. The first invocation will be metered for
            // 1000ms, with the rest not adding anything more.
            for (int i = 0; i < 10; i++)
            {
                publisher.OnFunctionStarted("foo", "1", now);
                now += TimeSpan.FromMilliseconds(100);
                publisher.OnFunctionCompleted("foo", "1", now);
            }

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(10, publisher.FunctionExecutionCount);
            Assert.Equal(1000, publisher.FunctionExecutionTimeMS);
        }

        [Fact]
        public void FunctionsStartStop_MinimumActivityIntervals_Scenario3()
        {
            var publisher = CreatePublisher(metricsPublishInterval: TimeSpan.FromHours(1), inStandbyMode: false);

            Assert.Equal(1000, _options.MinimumActivityIntervalMS);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(0, publisher.FunctionExecutionCount);
            Assert.Equal(0, publisher.FunctionExecutionTimeMS);

            DateTime now = DateTime.UtcNow;

            // two functions start and overlap for a while
            // the activity interval is 1.5s
            publisher.OnFunctionStarted("foo", "1", now);
            publisher.OnFunctionStarted("bar", "2", now);
            now += TimeSpan.FromMilliseconds(700);
            publisher.OnFunctionCompleted("foo", "1", now);
            now += TimeSpan.FromMilliseconds(800);
            publisher.OnFunctionCompleted("bar", "2", now);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(2, publisher.FunctionExecutionCount);
            Assert.Equal(1500, publisher.FunctionExecutionTimeMS);

            now += TimeSpan.FromMilliseconds(100);

            // a short invocation starts and completes, and with the duration
            // being rounded up to the minimum
            publisher.OnFunctionStarted("foo", "3", now);
            now += TimeSpan.FromMilliseconds(100);
            publisher.OnFunctionCompleted("foo", "3", now);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(3, publisher.FunctionExecutionCount);
            Assert.Equal(2500, publisher.FunctionExecutionTimeMS);

            // another invocation starts and completes, all within the
            // interval we've already metered (we're 900ms ahead)
            publisher.OnFunctionStarted("foo", "3", now);
            now += TimeSpan.FromMilliseconds(300);
            publisher.OnFunctionCompleted("foo", "3", now);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(4, publisher.FunctionExecutionCount);
            Assert.Equal(2500, publisher.FunctionExecutionTimeMS);

            // this invocation leaves a remainder of 100ms of the
            // previously metered interval
            publisher.OnFunctionStarted("foo", "3", now);
            now += TimeSpan.FromMilliseconds(500);
            publisher.OnFunctionCompleted("foo", "3", now);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(5, publisher.FunctionExecutionCount);
            Assert.Equal(2500, publisher.FunctionExecutionTimeMS);

            // this invocation completes the previous and starts a new
            // interval
            publisher.OnFunctionStarted("foo", "3", now);
            now += TimeSpan.FromMilliseconds(200);
            publisher.OnFunctionCompleted("foo", "3", now);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(6, publisher.FunctionExecutionCount);
            Assert.Equal(3500, publisher.FunctionExecutionTimeMS);

            // We're 900ms ahead. This final invocation completes that
            // then adds a final duration over the minimum, which
            // is rounded up to the next hundred ms
            publisher.OnFunctionStarted("foo", "3", now);
            now += TimeSpan.FromMilliseconds(900 + 1234);
            publisher.OnFunctionCompleted("foo", "3", now);

            Assert.Equal(0, publisher.ActiveFunctionCount);
            Assert.Equal(7, publisher.FunctionExecutionCount);
            Assert.Equal(4800, publisher.FunctionExecutionTimeMS);
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

        private void ValidateTotalTime(long value, long delay)
        {
            // Ensure the measured total time for the timer interval is within
            // the expected range (plus a small margin of error)
            // For these unit tests, the timer isn't actually running - we're
            // initiating the publish operations manually so we control the interval.
            Assert.InRange(value, 0, delay + 50);
        }
    }
}
