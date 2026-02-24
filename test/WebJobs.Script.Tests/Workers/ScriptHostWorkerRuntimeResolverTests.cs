// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
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
        public void GetWorkerRuntime_DoesNotCacheDefaultValue()
        {
            var environmentMock = new Mock<IEnvironment>(MockBehavior.Strict);
            environmentMock
                .Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime))
                .Returns((string)null);

            var scriptJobHostOptions = CreateOptionsMonitor(null);
            var resolver = new ScriptHostWorkerRuntimeResolver(environmentMock.Object, scriptJobHostOptions);

            var result1 = resolver.GetWorkerRuntime(defaultValue: string.Empty);
            var result2 = resolver.GetWorkerRuntime();

            Assert.Equal(string.Empty, result1);
            Assert.Null(result2);
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

        [Theory]
        [InlineData(null, "node", true)] // Environment variable lookup
        [InlineData("mcp-custom-handler", "custom", false)] // Custom handler profile
        [InlineData("web-app-custom-handler", "custom", false)] // Custom handler profile
        public async Task GetWorkerRuntime_IsThreadSafe_WhenCalledConcurrently(string configurationProfile, string expectedRuntime, bool shouldCallEnvironment)
        {
            // Arrange
            var environmentCallCount = 0;
            var environmentMock = new Mock<IEnvironment>(MockBehavior.Strict);

            if (shouldCallEnvironment)
            {
                environmentMock
                    .Setup(e => e.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime))
                    .Returns(() =>
                    {
                        System.Threading.Interlocked.Increment(ref environmentCallCount);
                        // Simulate some work to increase chance of race condition
                        System.Threading.Thread.Sleep(10);
                        return expectedRuntime;
                    });
            }
            else
            {
                // Environment should never be called for custom handler profiles
                environmentMock
                    .Setup(e => e.GetEnvironmentVariable(It.IsAny<string>()))
                    .Throws(new InvalidOperationException("Environment should not be accessed for custom handler profiles"));
            }

            var scriptJobHostOptions = CreateOptionsMonitor(configurationProfile);
            var resolver = new ScriptHostWorkerRuntimeResolver(environmentMock.Object, scriptJobHostOptions);

            const int taskCount = 10;

            // Create multiple tasks that will call GetWorkerRuntime concurrently
            var tasks = new Task<string>[taskCount];
            for (int i = 0; i < taskCount; i++)
            {
                tasks[i] = Task.Run(() => resolver.GetWorkerRuntime());
            }

            var results = await Task.WhenAll(tasks);

            // All tasks should get the same result
            Assert.All(results, result => Assert.Equal(expectedRuntime, result));

            if (shouldCallEnvironment)
            {
                // The environment variable should be read at least once, but due to thread-safety,
                // it might be read a few times if multiple threads enter the initialization path
                // before the first one completes. However, it should be significantly less than
                // the number of tasks if caching is working.
                Assert.InRange(environmentCallCount, 1, taskCount);

                int environmentVariableCallCountFinal = environmentCallCount;

                // Verify that subsequent calls use the cached value
                var cachedResult = resolver.GetWorkerRuntime();
                Assert.Equal(expectedRuntime, cachedResult);

                // Environment call count should not increase after caching is done
                Assert.Equal(environmentVariableCallCountFinal, environmentCallCount);
            }
            else
            {
                // Verify environment was never accessed for custom handler profiles
                environmentMock.Verify(e => e.GetEnvironmentVariable(It.IsAny<string>()), Times.Never);
            }
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
