// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text.Json;
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
        public void Configure_WithRealEnvironmentValues_SetsCorrectValues()
        {
            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                });
            var configuration = configBuilder.Build();

            var setup = new WorkerConfigurationResolverOptionsSetup(configuration, mockScriptHostManager.Object);
            var options = new WorkerConfigurationResolverOptions();

            setup.Configure(options);

            Assert.Equal("/default/workers", options.WorkersDirPath);
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

            string json = options.Format();

            Assert.NotNull(json);
            Assert.NotEmpty(json);

            var jsonDocument = JsonDocument.Parse(json);
            Assert.NotNull(jsonDocument);

            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("WorkersDirPath", out var workersDirPathProperty));
            Assert.Equal(null, workersDirPathProperty.GetString());
        }
    }
}
