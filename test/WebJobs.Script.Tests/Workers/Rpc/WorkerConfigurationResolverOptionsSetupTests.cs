// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WorkerConfigurationResolverOptionsSetupTests
    {
        [Fact]
        public void Configure_WithEnvironmentValues_SetsCorrectValues()
        {
            var loggerFactory = WorkerConfigurationResolverTestsHelper.GetTestLoggerFactory();
            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                }).Build();

            var setup = new WorkerConfigurationResolverOptionsSetup(loggerFactory, configuration, mockScriptHostManager.Object, FileUtility.Instance);
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            Assert.Equal("/default/workers", options.WorkersRootDirPath);
        }

        [Fact]
        public void Configure_WithEnvironmentValues_UpdatedConfiguration_SetsCorrectValues()
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var mockServiceProvider = new Mock<IServiceProvider>();
            var configuration = new ConfigurationBuilder().Build();

            var latestConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                }).Build();

            mockServiceProvider.Setup(sp => sp.GetService(typeof(IConfiguration))).Returns(latestConfiguration);
            mockScriptHostManager.As<IServiceProvider>()
                .Setup(sp => sp.GetService(typeof(IConfiguration)))
                .Returns(latestConfiguration);

            var setup = new WorkerConfigurationResolverOptionsSetup(loggerFactory, configuration, mockScriptHostManager.Object, FileUtility.Instance);
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            var logs = loggerProvider.GetAllLogMessages();

            Assert.Equal("/default/workers", options.WorkersRootDirPath);
            Assert.Single(logs.Where(l => l.FormattedMessage == "Found configuration section 'languageWorkers:workersDirectory' in 'latestConfiguration'."));
        }

        [Fact]
        public void Configure_WithEnvironmentValues_WithConfiguration_SetsCorrectValues()
        {
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var mockServiceProvider = new Mock<IServiceProvider>();
            var configuration = new ConfigurationBuilder()
                                    .AddInMemoryCollection(new Dictionary<string, string>
                                    {
                                        [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers",
                                    })
                                    .Build();

            var latestConfiguration = new ConfigurationBuilder().Build();

            mockServiceProvider.Setup(sp => sp.GetService(typeof(IConfiguration))).Returns(latestConfiguration);
            mockScriptHostManager.As<IServiceProvider>()
                .Setup(sp => sp.GetService(typeof(IConfiguration)))
                .Returns(latestConfiguration);

            var setup = new WorkerConfigurationResolverOptionsSetup(loggerFactory, configuration, mockScriptHostManager.Object, FileUtility.Instance);
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            var logs = loggerProvider.GetAllLogMessages();

            Assert.Equal("/default/workers", options.WorkersRootDirPath);
            Assert.Single(logs.Where(l => l.FormattedMessage == "Found configuration section 'languageWorkers:workersDirectory' in '_configuration'."));
        }

        [Fact]
        public void Configure_WithNullConfigValues_SetsCorrectValues()
        {
            var testLoggerFactory = WorkerConfigurationResolverTestsHelper.GetTestLoggerFactory();
            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = null,
                }).Build();

            var setup = new WorkerConfigurationResolverOptionsSetup(testLoggerFactory, configuration, mockScriptHostManager.Object, FileUtility.Instance);
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            Assert.NotNull(options.WorkersRootDirPath);
            Assert.Contains("workers", options.WorkersRootDirPath);
        }

        [Fact]
        public void Configure_WorkerConfigurationResolverOptions()
        {
            var testLoggerFactory = WorkerConfigurationResolverTestsHelper.GetTestLoggerFactory();
            var testEnvironment = new TestEnvironment();
            var mockScriptHostManager = new Mock<IScriptHostManager>();
            var configuration = new ConfigurationBuilder().Build();

            var setup = new WorkerConfigurationResolverOptionsSetup(testLoggerFactory, configuration, mockScriptHostManager.Object, FileUtility.Instance);
            var options = new WorkerConfigurationResolverOptions();
            setup.Configure(options);

            Assert.NotNull(options.WorkersRootDirPath);
            Assert.Contains("workers", options.WorkersRootDirPath);
        }

        [Fact]
        public void Format_SerializesOptionsToJson()
        {
            var options = new WorkerConfigurationResolverOptions
            {
                WorkersRootDirPath = "/test/workers"
            };

            string json = options.Format();

            Assert.NotNull(json);
            Assert.NotEmpty(json);

            var jsonDocument = JsonDocument.Parse(json);
            Assert.NotNull(jsonDocument);

            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("WorkersRootDirPath", out var workersDirPathProperty));
            Assert.Equal("/test/workers", workersDirPathProperty.GetString());
        }

        [Fact]
        public void Format_WithNullProperties_SerializesSuccessfully()
        {
            var options = new WorkerConfigurationResolverOptions
            {
                WorkersRootDirPath = null
            };

            string json = options.Format();

            Assert.NotNull(json);
            Assert.NotEmpty(json);

            var jsonDocument = JsonDocument.Parse(json);
            Assert.NotNull(jsonDocument);

            var root = jsonDocument.RootElement;
            Assert.True(root.TryGetProperty("WorkersRootDirPath", out var workersDirPathProperty));
            Assert.Equal(null, workersDirPathProperty.GetString());
        }
    }
}
