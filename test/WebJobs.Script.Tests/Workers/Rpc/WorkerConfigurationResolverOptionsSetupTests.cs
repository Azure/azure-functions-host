// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class WorkerConfigurationResolverOptionsSetupTests
    {
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<IEnvironment> _mockEnvironment;
        private readonly Mock<IScriptHostManager> _mockScriptHostManager;
        private readonly WorkerConfigurationResolverOptionsSetup _setup;

        public WorkerConfigurationResolverOptionsSetupTests()
        {
            _mockConfiguration = new Mock<IConfiguration>();
            _mockEnvironment = new Mock<IEnvironment>();
            _mockScriptHostManager = new Mock<IScriptHostManager>();
            var hostingOptions = new FunctionsHostingConfigOptions();

            _setup = new WorkerConfigurationResolverOptionsSetup(_mockConfiguration.Object, _mockEnvironment.Object, _mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
        }

        [Fact]
        public void Configure_WithRealEnvironmentValues_SetsCorrectDefaults()
        {
            // Arrange
            var testEnvironment = new TestEnvironment();
            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/default/workers"
                });
            var configuration = configBuilder.Build();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            var hostingOptions = new FunctionsHostingConfigOptions();

            var setup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, mockScriptHostManager.Object, new OptionsWrapper<FunctionsHostingConfigOptions>(hostingOptions));
            var options = new WorkerConfigurationResolverOptions();

            // Act
            setup.Configure(options);

            // Assert
            Assert.Null(options.WorkerRuntime); // No worker runtime set
            Assert.Equal(ScriptConstants.LatestPlatformChannelNameUpper, options.ReleaseChannel); // Default release channel
            Assert.False(options.IsPlaceholderModeEnabled); // Default placeholder mode
            Assert.False(options.IsMultiLanguageWorkerEnvironment); // Default multi-language mode
            Assert.Equal("/default/workers", options.WorkersDirPath);
            Assert.NotNull(options.LanguageSection);
        }
    }
}