// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WorkerConfigurationResolverOptionsSetupTests
    {
        [Fact]
        public void Configure_WithRealEnvironmentValues_SetsCorrectDefaults()
        {
            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{RpcWorkerConstants.WorkerProbingPathsSectionName}:0"] = "testPath1",
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{RpcWorkerConstants.WorkerProbingPathsSectionName}:1"] = "testPath2",
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{RpcWorkerConstants.WorkerProbingPathsSectionName}:2"] = " ",
                });
            var configuration = configBuilder.Build();

            var setup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, mockScriptHostManager.Object, null);
            var options = new WorkerConfigurationResolverOptions();

            setup.Configure(options);

            // Assert
            Assert.Null(options.WorkerRuntime);
            Assert.Equal(ScriptConstants.LatestPlatformChannelNameUpper, options.ReleaseChannel);
            Assert.False(options.IsPlaceholderModeEnabled);
            Assert.False(options.IsMultiLanguageWorkerEnvironment);
            Assert.Equal("/default/workers", options.WorkersDirPath);
            Assert.NotNull(options.LanguageWorkersSettings);

            Assert.Equal(2, options.ProbingPaths.Count);
            Assert.True(options.ProbingPaths.Contains("testPath1"));
            Assert.True(options.ProbingPaths.Contains("testPath2"));

            Assert.True(options.WorkersAvailableForResolution.Count == 0);
        }

        [Fact]
        public void Format_SerializesOptionsToJson()
        {
            var options = new WorkerConfigurationResolverOptions
            {
                WorkersDirPath = "/test/workers"
            };

            string json = options.Format();

            Assert.NotNull(json);
            Assert.NotEmpty(json);

            var jsonDocument = JsonDocument.Parse(json);
            Assert.NotNull(jsonDocument);

            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("WorkersDirPath", out var workersDirPathProperty));
            Assert.Equal("/test/workers", workersDirPathProperty.GetString());
        }

        [Fact]
        public void Format_WithNullProperties_SerializesSuccessfully()
        {
            var options = new WorkerConfigurationResolverOptions
            {
                WorkersDirPath = null
            };

            var hostingOptions = new FunctionsHostingConfigOptions();
            hostingOptions.Features.Add(RpcWorkerConstants.WorkersAvailableForDynamicResolution, "java|node");
            string json = options.Format();

            Assert.NotNull(json);
            Assert.NotEmpty(json);

            var jsonDocument = JsonDocument.Parse(json);
            Assert.NotNull(jsonDocument);

            // Assert
       //     Assert.Equal("java", options.WorkerRuntime);
            Assert.Equal("standard", options.ReleaseChannel);
            Assert.False(options.IsPlaceholderModeEnabled);
            Assert.False(options.IsMultiLanguageWorkerEnvironment);
            Assert.Equal("/default/workers", options.WorkersDirPath);
            Assert.NotNull(options.LanguageWorkersSettings);

            Assert.NotNull(options.ProbingPaths);
            Assert.True(options.ProbingPaths.Count == 0);

            Assert.True(options.WorkersAvailableForResolution.Count == 2);
            Assert.True(options.WorkersAvailableForResolution.Contains("java"));
            Assert.True(options.WorkersAvailableForResolution.Contains("node"));
        }
    }
}
