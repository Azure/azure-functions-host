// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.FlexConsumptionMetricsTests)]
    public class MetricsEndToEndTests_FlexConsumption : IClassFixture<MetricsEndToEndTests_FlexConsumption.TestFixture>
    {
        private TestFixture _fixture;

        public MetricsEndToEndTests_FlexConsumption(TestFixture fixture)
        {
            _fixture = fixture;

            // reset values that the tests are configuring
            var metricsPublisher = (FlexConsumptionMetricsPublisher)_fixture.Host.JobHostServices.GetService<IMetricsPublisher>();
            metricsPublisher.IsAlwaysReady = false;
            metricsPublisher.MetricsFilePath = _fixture.MetricsPublishPath;

            _fixture.CleanupMetricsFiles();
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public async Task ShortTestRun_ExpectedMetricsGenerated(bool isAlwaysReadyInstance, bool metricsPublishEnabled)
        {
            var metricsPublisher = (FlexConsumptionMetricsPublisher)_fixture.Host.JobHostServices.GetService<IMetricsPublisher>();
            metricsPublisher.IsAlwaysReady = isAlwaysReadyInstance;

            if (!metricsPublishEnabled)
            {
                metricsPublisher.MetricsFilePath = null;
            }

            int delay = 2500;
            int executionCount = 0;
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < delay)
            {
                await InvokeTestFunction();
                executionCount++;

                await Task.Delay(50);
            }

            var options = _fixture.Host.WebHostServices.GetService<IOptions<FlexConsumptionMetricsPublisherOptions>>();
            int expectedFileCount = (delay / options.Value.MetricsPublishIntervalMS);

            // wait for final file to be written
            await Task.Delay(1000);

            // verify metrics files were written with expected content
            var directory = new DirectoryInfo(_fixture.MetricsPublishPath);
            var files = directory.GetFiles();

            if (metricsPublishEnabled)
            {
                Assert.True(files.Length >= expectedFileCount);

                long totalPublishedExecutionCount = 0;
                long totalPublishedExecutionTime = 0;
                foreach (var file in files)
                {
                    var metrics = await ReadMetricsAsync(file.FullName);

                    // verify the execution time
                    Assert.Equal(metrics.ExecutionCount * FlexConsumptionMetricsPublisherOptions.DefaultMinimumActivityIntervalMS, metrics.ExecutionTimeMS);
                    ValidateTotalTime(metrics.TotalTimeMS, options.Value.MetricsPublishIntervalMS);
                    Assert.Equal(isAlwaysReadyInstance, metrics.IsAlwaysReady);

                    totalPublishedExecutionCount += metrics.ExecutionCount;
                    totalPublishedExecutionTime += metrics.ExecutionTimeMS;
                }

                Assert.Equal(executionCount, totalPublishedExecutionCount);
                Assert.Equal(totalPublishedExecutionCount * FlexConsumptionMetricsPublisherOptions.DefaultMinimumActivityIntervalMS, totalPublishedExecutionTime);
            }
            else
            {
                Assert.Empty(files);
            }

            // verify expected publishing logs are written
            var logs = _fixture.Host.GetWebHostLogMessages();
            var metricsLogs = logs.Where(p => p.Category == typeof(FlexConsumptionMetricsPublisher).FullName).ToArray();
            var publishingLogs = metricsLogs.Where(p => p.EventId.Name == "PublishingMetrics").ToArray();
            Assert.True(publishingLogs.Length >= expectedFileCount);
        }

        [Fact]
        public async Task NoFunctionActivity_AlwaysReadyInstance_ExpectedMetricsPublished()
        {
            var metricsPublisher = (FlexConsumptionMetricsPublisher)_fixture.Host.JobHostServices.GetService<IMetricsPublisher>();
            metricsPublisher.IsAlwaysReady = true;

            int delay = 2500;

            var options = _fixture.Host.WebHostServices.GetService<IOptions<FlexConsumptionMetricsPublisherOptions>>();
            int expectedFileCount = (delay / options.Value.MetricsPublishIntervalMS);

            await Task.Delay(delay);

            // verify metrics files were written with expected content
            var directory = new DirectoryInfo(_fixture.MetricsPublishPath);
            var files = directory.GetFiles();
            Assert.True(files.Length >= expectedFileCount);

            foreach (var file in files)
            {
                var metrics = await ReadMetricsAsync(file.FullName);

                Assert.Equal(0, metrics.ExecutionTimeMS);
                Assert.Equal(0, metrics.ExecutionCount);
                ValidateTotalTime(metrics.TotalTimeMS, options.Value.MetricsPublishIntervalMS);
                Assert.True(metrics.IsAlwaysReady);
            }
        }

        [Fact]
        public async Task ShortTestRun_MaximumFileLimitHonored()
        {
            // write some initial files that put us over the limit
            var options = _fixture.Host.WebHostServices.GetService<IOptions<FlexConsumptionMetricsPublisherOptions>>();
            int initialFileCount = options.Value.MaxFileCount + 5;

            var fileSystem = new FileSystem();
            for (int i = 0; i < initialFileCount; i++)
            {
                string fileName = $"{Guid.NewGuid().ToString().ToLower()}.json";
                string filePath = Path.Combine(options.Value.MetricsFilePath, fileName);
                var sw = fileSystem.File.CreateText(filePath);
                sw.Close();
            }

            int totalMilliseconds = 5000;
            int executionCount = 0;
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < totalMilliseconds)
            {
                await InvokeTestFunction();
                executionCount++;

                await Task.Delay(50);
            }

            // wait for final file to be written
            await Task.Delay(1000);

            // ensure we stay under the file limit
            var directory = new DirectoryInfo(_fixture.MetricsPublishPath);
            var files = directory.GetFiles();
            Assert.Equal(files.Length, options.Value.MaxFileCount);

            // verify logs
            var logs = _fixture.Host.GetWebHostLogMessages();
            var metricsLogs = logs.Where(p => p.Category == typeof(FlexConsumptionMetricsPublisher).FullName).ToArray();
            var log = metricsLogs.Single(p => p.FormattedMessage == $"Starting metrics publisher (AlwaysReady={false}, MetricsPath='{_fixture.MetricsPublishPath}').");
            Assert.Equal(LogLevel.Information, log.Level);

            log = metricsLogs.Single(p => p.FormattedMessage == "Deleting 6 metrics file(s).");
            Assert.Equal(LogLevel.Debug, log.Level);

            int count = metricsLogs.Count(p => p.FormattedMessage == "Deleting 1 metrics file(s).");
            Assert.True(count > 1);

            var publishingLogs = metricsLogs.Where(p => p.FormattedMessage.StartsWith("Publishing metrics")).ToArray();
            Assert.True(count > 1);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NoFunctionActivity_ExpectedMetricsGenerated(bool isAlwaysReadyInstance)
        {
            if (isAlwaysReadyInstance)
            {
                var metricsPublisher = (FlexConsumptionMetricsPublisher)_fixture.Host.JobHostServices.GetService<IMetricsPublisher>();
                metricsPublisher.IsAlwaysReady = true;
            }

            var directory = new DirectoryInfo(_fixture.MetricsPublishPath);
            var files = directory.GetFiles();

            Assert.Equal(files.Length, 0);

            await Task.Delay(1000);

            files = directory.GetFiles();

            if (!isAlwaysReadyInstance)
            {
                Assert.Equal(files.Length, 0);
            }
            else
            {
                Assert.True(files.Length > 0);

                var metrics = await ReadMetricsAsync(files[0].FullName);

                var options = _fixture.Host.WebHostServices.GetService<IOptions<FlexConsumptionMetricsPublisherOptions>>();
                ValidateTotalTime(metrics.TotalTimeMS, options.Value.MetricsPublishIntervalMS);
                Assert.Equal(0, metrics.ExecutionCount);
                Assert.Equal(0, metrics.ExecutionTimeMS);
                Assert.True(metrics.IsAlwaysReady);
            };
        }

        private async Task InvokeTestFunction()
        {
            string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger");
            string uri = $"api/httptrigger?code={functionKey}&name=Mathew";

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
            HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private static async Task<FlexConsumptionMetricsPublisher.Metrics> ReadMetricsAsync(string metricsFilePath)
        {
            string content = await File.ReadAllTextAsync(metricsFilePath);
            return JsonConvert.DeserializeObject<FlexConsumptionMetricsPublisher.Metrics>(content);
        }

        private static void ValidateTotalTime(long value, long metricsPublishIntervalMS)
        {
            // The publish timer is set to MetricsPublishIntervalMS, so the measured total time for the timer
            // intervals should be within a small margin of error from that
            Assert.InRange(value, 0, metricsPublishIntervalMS + 50);
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "csharp"), "samples", RpcWorkerConstants.DotNetLanguageWorkerName)
            {
            }

            public string MetricsPublishPath { get; private set; }

            public override void ConfigureWebHost(IServiceCollection services)
            {
                base.ConfigureWebHost(services);

                services.Configure<FlexConsumptionMetricsPublisherOptions>(options =>
                {
                    options.MetricsPublishIntervalMS = 500;
                    options.InitialPublishDelayMS = 0;
                    options.MaxFileCount = 10;
                });

                MetricsPublishPath = Path.Combine(Path.GetTempPath(), "metrics");

                var environment = new TestEnvironment();
                string testSiteName = "somewebsite";
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagAllowSynchronousIO);
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, testSiteName);
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformVersionWindows, "89.0.7.73");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresComputerName, "RD281878FCB8E7");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.FlexConsumptionSku);
                environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsMetricsPublishPath, MetricsPublishPath);

                // have to set these statically here because some APIs in the host aren't going through IEnvironment
                string key = TestHelpers.GenerateKeyHexString();
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, key);
                Environment.SetEnvironmentVariable("AzureWebEncryptionKey", key);
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, testSiteName);

                services.AddSingleton<IEnvironment>(_ => environment);
            }

            public override void ConfigureScriptHost(IServiceCollection services)
            {
                base.ConfigureScriptHost(services);

                // the test host by default registers a mock, but we want the actual logger for these tests
                services.AddSingleton<IMetricsLogger, WebHostMetricsLogger>();
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger"
                    };
                });
            }

            public void CleanupMetricsFiles()
            {
                var directory = new DirectoryInfo(MetricsPublishPath);

                if (!directory.Exists)
                {
                    return;
                }

                foreach (var file in directory.GetFiles())
                {
                    file.Delete();
                }
            }

            public override async Task DisposeAsync()
            {
                await base.DisposeAsync();

                CleanupMetricsFiles();
            }
        }
    }
}
