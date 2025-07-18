// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            _setup = new WorkerConfigurationResolverOptionsSetup(_mockConfiguration.Object, _mockEnvironment.Object, _mockScriptHostManager.Object);
        }

        /*
        [Fact]
        public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new WorkerConfigurationResolverOptionsSetup(null, _mockEnvironment.Object, _mockScriptHostManager.Object));
        }

        [Fact]
        public void Constructor_WithNullEnvironment_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new WorkerConfigurationResolverOptionsSetup(_mockConfiguration.Object, null, _mockScriptHostManager.Object));
        }

        [Fact]
        public void Constructor_WithNullScriptHostManager_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new WorkerConfigurationResolverOptionsSetup(_mockConfiguration.Object, _mockEnvironment.Object, null));
        }

        [Fact]
        public void Configure_SetsWorkerRuntime_FromEnvironment()
        {
            // Arrange
            const string expectedWorkerRuntime = "node";
            _mockEnvironment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime))
                .Returns(expectedWorkerRuntime);

            SetupBasicMocks();
            var options = new WorkerConfigurationResolverOptions();

            // Act
            _setup.Configure(options);

            // Assert
            Assert.Equal(expectedWorkerRuntime, options.WorkerRuntime);
        }

        [Theory]
        [InlineData("LATEST")]
        [InlineData("STANDARD")]
        [InlineData("EXTENDED")]
        [InlineData(null)]
        [InlineData("")]
        public void Configure_SetsReleaseChannel_FromEnvironment(string releaseChannel)
        {
            // Arrange
            _mockEnvironment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel))
                .Returns(releaseChannel);

            SetupBasicMocks();
            var options = new WorkerConfigurationResolverOptions();

            // Act
            _setup.Configure(options);

            // Assert
            string expectedChannel = string.IsNullOrEmpty(releaseChannel) ? ScriptConstants.LatestPlatformChannelNameUpper : releaseChannel.ToUpperInvariant();
            Assert.Equal(expectedChannel, options.ReleaseChannel);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Configure_SetsIsPlaceholderModeEnabled_FromEnvironment(bool isPlaceholderMode)
        {
            // Arrange
            _mockEnvironment.Setup(e => e.IsPlaceholderModeEnabled()).Returns(isPlaceholderMode);

            SetupBasicMocks();
            var options = new WorkerConfigurationResolverOptions();

            // Act
            _setup.Configure(options);

            // Assert
            Assert.Equal(isPlaceholderMode, options.IsPlaceholderModeEnabled);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Configure_SetsIsMultiLanguageWorkerEnvironment_FromEnvironment(bool isMultiLanguage)
        {
            // Arrange
            _mockEnvironment.Setup(e => e.IsMultiLanguageRuntimeEnvironment()).Returns(isMultiLanguage);

            SetupBasicMocks();
            var options = new WorkerConfigurationResolverOptions();

            // Act
            _setup.Configure(options);

            // Assert
            Assert.Equal(isMultiLanguage, options.IsMultiLanguageWorkerEnvironment);
        }

        [Fact]
        public void Configure_SetsLanguageSection_FromConfiguration()
        {
            // Arrange
            var mockLanguageSection = new Mock<IConfigurationSection>();
            _mockConfiguration.Setup(c => c.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}"))
                .Returns(mockLanguageSection.Object);

            SetupBasicMocks();
            var options = new WorkerConfigurationResolverOptions();

            // Act
            _setup.Configure(options);

            // Assert
            Assert.Equal(mockLanguageSection.Object, options.LanguageSection);
        }

        [Fact]
        public void Configure_SetsWorkersDirPath_FromLanguageSection()
        {
            // Arrange
            const string expectedPath = "/custom/workers/path";
            var mockLanguageSection = new Mock<IConfigurationSection>();
            var mockWorkersSection = new Mock<IConfigurationSection>();
            mockWorkersSection.Setup(s => s.Value).Returns(expectedPath);

            mockLanguageSection.Setup(s => s.GetSection(WorkerConstants.WorkersDirectorySectionName))
                .Returns(mockWorkersSection.Object);

            _mockConfiguration.Setup(c => c.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}"))
                .Returns(mockLanguageSection.Object);

            SetupBasicMocks();
            var options = new WorkerConfigurationResolverOptions();

            // Act
            _setup.Configure(options);

            // Assert
            Assert.Equal(expectedPath, options.WorkersDirPath);
        }

        [Fact]
        public void Configure_UsesOriginalConfiguration_WhenScriptHostManagerIsNotServiceProvider()
        {
            // Arrange
            var mockLanguageSection = new Mock<IConfigurationSection>();
            _mockConfiguration.Setup(c => c.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}"))
                .Returns(mockLanguageSection.Object);

            // ScriptHostManager is not a service provider
            _mockScriptHostManager.Setup(s => s as IServiceProvider).Returns((IServiceProvider)null);

            SetupBasicMocks();
            var options = new WorkerConfigurationResolverOptions();

            // Act
            _setup.Configure(options);

            // Assert
            Assert.Equal(mockLanguageSection.Object, options.LanguageSection);
            _mockConfiguration.Verify(c => c.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}"), Times.Once);
        }

        [Fact]
        public void Configure_UsesLatestConfiguration_WhenScriptHostManagerProvidesNewConfiguration()
        {
            // Arrange
            var mockServiceProvider = new Mock<IServiceProvider>();
            var mockLatestConfiguration = new Mock<IConfiguration>();
            var mockLatestLanguageSection = new Mock<IConfigurationSection>();

            mockServiceProvider.Setup(sp => sp.GetService<IConfiguration>())
                .Returns(mockLatestConfiguration.Object);

            mockLatestConfiguration.Setup(c => c.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}"))
                .Returns(mockLatestLanguageSection.Object);

            _mockScriptHostManager.As<IServiceProvider>()
                .Setup(sp => sp.GetService<IConfiguration>())
                .Returns(mockLatestConfiguration.Object);

            SetupBasicMocks();
            var options = new WorkerConfigurationResolverOptions();

            // Act
            _setup.Configure(options);

            // Assert
            Assert.Equal(mockLatestLanguageSection.Object, options.LanguageSection);
        }

        [Fact]
        public void Configure_FallsBackToOriginalConfiguration_WhenScriptHostManagerReturnsNullConfiguration()
        {
            // Arrange
            var mockLanguageSection = new Mock<IConfigurationSection>();
            _mockConfiguration.Setup(c => c.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}"))
                .Returns(mockLanguageSection.Object);

            _mockScriptHostManager.As<IServiceProvider>()
                .Setup(sp => sp.GetService<IConfiguration>())
                .Returns((IConfiguration)null);

            SetupBasicMocks();
            var options = new WorkerConfigurationResolverOptions();

            // Act
            _setup.Configure(options);

            // Assert
            Assert.Equal(mockLanguageSection.Object, options.LanguageSection);
            _mockConfiguration.Verify(c => c.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}"), Times.Once);
        }

        [Theory]
        [InlineData("node", "LATEST", true, false)]
        [InlineData("python", "STANDARD", false, true)]
        [InlineData("java", "EXTENDED", true, true)]
        [InlineData(null, null, false, false)]
        public void Configure_SetsAllProperties_Correctly(string workerRuntime, string releaseChannel, bool isPlaceholder, bool isMultiLanguage)
        {
            // Arrange
            _mockEnvironment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime))
                .Returns(workerRuntime);
            _mockEnvironment.Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel))
                .Returns(releaseChannel);
            _mockEnvironment.Setup(e => e.IsPlaceholderModeEnabled()).Returns(isPlaceholder);
            _mockEnvironment.Setup(e => e.IsMultiLanguageRuntimeEnvironment()).Returns(isMultiLanguage);

            var mockLanguageSection = new Mock<IConfigurationSection>();
            var mockWorkersSection = new Mock<IConfigurationSection>();
            const string expectedPath = "/test/workers";
            mockWorkersSection.Setup(s => s.Value).Returns(expectedPath);

            mockLanguageSection.Setup(s => s.GetSection(WorkerConstants.WorkersDirectorySectionName))
                .Returns(mockWorkersSection.Object);

            _mockConfiguration.Setup(c => c.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}"))
                .Returns(mockLanguageSection.Object);

            var options = new WorkerConfigurationResolverOptions();

            // Act
            _setup.Configure(options);

            // Assert
            Assert.Equal(workerRuntime, options.WorkerRuntime);
            string expectedChannel = string.IsNullOrEmpty(releaseChannel) ? ScriptConstants.LatestPlatformChannelNameUpper : releaseChannel.ToUpperInvariant();
            Assert.Equal(expectedChannel, options.ReleaseChannel);
            Assert.Equal(isPlaceholder, options.IsPlaceholderModeEnabled);
            Assert.Equal(isMultiLanguage, options.IsMultiLanguageWorkerEnvironment);
            Assert.Equal(expectedPath, options.WorkersDirPath);
            Assert.Equal(mockLanguageSection.Object, options.LanguageSection);
        }
        */

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

            var setup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, mockScriptHostManager.Object);
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

        [Fact]
        public void Configure_WithEnvironmentVariables_OverridesDefaults()
        {
            // Arrange
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "java");
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel, "STANDARD");
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AppKind, ScriptConstants.WorkFlowAppKind);

            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [$"{RpcWorkerConstants.LanguageWorkersSectionName}:{WorkerConstants.WorkersDirectorySectionName}"] = "/custom/workers"
                });
            var configuration = configBuilder.Build();
            var mockScriptHostManager = new Mock<IScriptHostManager>();

            var setup = new WorkerConfigurationResolverOptionsSetup(configuration, testEnvironment, mockScriptHostManager.Object);
            var options = new WorkerConfigurationResolverOptions();

            // Act
            setup.Configure(options);

            // Assert
            Assert.Equal("java", options.WorkerRuntime);
            Assert.Equal("STANDARD", options.ReleaseChannel);
            Assert.True(options.IsPlaceholderModeEnabled);
            Assert.True(options.IsMultiLanguageWorkerEnvironment);
            Assert.Equal("/custom/workers", options.WorkersDirPath);
            Assert.NotNull(options.LanguageSection);
        }

        private void SetupBasicMocks()
        {
            _mockEnvironment.Setup(e => e.GetEnvironmentVariable(It.IsAny<string>())).Returns((string)null);
            _mockEnvironment.Setup(e => e.IsPlaceholderModeEnabled()).Returns(false);
            _mockEnvironment.Setup(e => e.IsMultiLanguageRuntimeEnvironment()).Returns(false);

            var mockLanguageSection = new Mock<IConfigurationSection>();
            var mockWorkersSection = new Mock<IConfigurationSection>();
            mockWorkersSection.Setup(s => s.Value).Returns((string)null);

            mockLanguageSection.Setup(s => s.GetSection(WorkerConstants.WorkersDirectorySectionName))
                .Returns(mockWorkersSection.Object);

            _mockConfiguration.Setup(c => c.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}"))
                .Returns(mockLanguageSection.Object);
        }
    }
}