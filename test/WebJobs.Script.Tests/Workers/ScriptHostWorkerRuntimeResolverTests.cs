// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    public sealed class ScriptHostWorkerRuntimeResolverTests
    {
        [Theory]
        // These 2 special configuration profiles should always resolve to "custom"
        [InlineData("mcp-custom-handler", "custom")]
        [InlineData("web-app-custom-handler", "custom")]

        // Any other configuration profile should fall back to the environment variable
        [InlineData("default", "node", true)]
        [InlineData("", "node", true)]
        [InlineData(null, "node", true)]
        public void GetWorkerRuntime_UsesCustomHandlerProfile_WhenConfigurationProfileIsPresent(string configurationProfile, string expectedRuntime, bool expectedToReadFromEnvironment = false)
        {
            var environmentMock = new Mock<IEnvironment>(MockBehavior.Strict);
            environmentMock
                .Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime))
                .Returns("node");
            var scriptJobHostOptions = CreateOptionsMonitor(configurationProfile);
            var resolver = new ScriptHostWorkerRuntimeResolver(environmentMock.Object, scriptJobHostOptions);

            var result = resolver.GetWorkerRuntime();

            Assert.Equal(expectedRuntime, result);

            if (expectedToReadFromEnvironment)
            {
                environmentMock.Verify(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime), Times.Once);
            }
            else
            {
                environmentMock.Verify(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime), Times.Never);
            }
        }

        [Fact]
        public void GetWorkerRuntime_NoConfigurationProfileOrEnvironment_ReturnsDefaultValue()
        {
            // No configuration profile and no environment variable
            var environmentMock = new Mock<IEnvironment>(MockBehavior.Strict);
            environmentMock
                .Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime))
                .Returns((string)null);

            var scriptJobHostOptions = CreateOptionsMonitor(null);
            var resolver = new ScriptHostWorkerRuntimeResolver(environmentMock.Object, scriptJobHostOptions);

            var result = resolver.GetWorkerRuntime("python");

            environmentMock.Verify(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime), Times.Once);
            Assert.Equal("python", result);
        }

        [Fact]
        public void GetWorkerRuntime_NoConfigurationProfileOrEnvironmentOrDefault_ReturnsNullRuntime()
        {
            // No configuration profile and no environment variable
            var environmentMock = new Mock<IEnvironment>(MockBehavior.Strict);
            environmentMock
                .Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime))
                .Returns((string)null);

            var scriptJobHostOptions = CreateOptionsMonitor(null);
            var resolver = new ScriptHostWorkerRuntimeResolver(environmentMock.Object, scriptJobHostOptions);

            var result = resolver.GetWorkerRuntime();

            Assert.Null(result);
            environmentMock.Verify(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime), Times.Once);
        }

        [Fact]
        public void GetWorkerRuntime_CachesEnvironmentValue()
        {
            var environmentMock = new Mock<IEnvironment>(MockBehavior.Strict);
            environmentMock
                .Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime))
                .Returns("node");

            var scriptJobHostOptions = CreateOptionsMonitor(null);
            var resolver = new ScriptHostWorkerRuntimeResolver(environmentMock.Object, scriptJobHostOptions);

            var result1 = resolver.GetWorkerRuntime();
            var result2 = resolver.GetWorkerRuntime();

            environmentMock.Verify(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime), Times.Once);
            Assert.Equal("node", result1);
            Assert.Equal("node", result2);
        }

        [Fact]
        public void Ctor_ThrowsArgumentNullException_WhenEnvironmentIsNull()
        {
            var scriptJobHostOptions = CreateOptionsMonitor(null);

            var exception = Assert.Throws<ArgumentNullException>(() =>
                new ScriptHostWorkerRuntimeResolver(null, scriptJobHostOptions));

            Assert.Equal("environment", exception.ParamName);
        }

        [Fact]
        public void Ctor_ThrowsArgumentNullException_WhenOptionsMonitorIsNull()
        {
            var environment = new TestEnvironment();

            var exception = Assert.Throws<ArgumentNullException>(() =>
                new ScriptHostWorkerRuntimeResolver(environment, null));

            Assert.Equal("scriptJobHostOptionsMonitor", exception.ParamName);
        }

        private static IOptionsMonitor<ScriptJobHostOptions> CreateOptionsMonitor(string configurationProfile)
        {
            var optionsMock = new Mock<IOptionsMonitor<ScriptJobHostOptions>>();
            optionsMock.Setup(o => o.CurrentValue)
                .Returns(new ScriptJobHostOptions { ConfigurationProfile = configurationProfile });
            return optionsMock.Object;
        }
    }
}
