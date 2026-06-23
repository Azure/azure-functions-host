// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Eventing
{
    public class DeferredLoggerProviderTests
    {
        [Fact]
        public void CreateLogger_ReturnsDeferredLogger_WhenEnabled()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            var provider = new DeferredLoggerProvider(new DeferredLogSource(), testEnvironment);

            var logger = provider.CreateLogger("TestCategory");

            Assert.IsType<DeferredLogger>(logger);
        }

        [Fact]
        public void CreateLogger_ReturnsNullLogger_AfterDispose()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            var provider = new DeferredLoggerProvider(new DeferredLogSource(), testEnvironment);
            provider.Dispose();

            var logger = provider.CreateLogger("TestCategory");

            Assert.IsType<NullLogger>(logger);
        }

        [Fact]
        public void Count_ReflectsBufferedErrorLogs()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            var provider = new DeferredLoggerProvider(new DeferredLogSource(), testEnvironment);

            var logger = provider.CreateLogger("TestCategory");
            logger.LogError("Test Log 1");
            logger.LogError("Test Log 2");

            Assert.Equal(2, provider.Count);
        }

        [Fact]
        public void Count_IgnoresLogsBelowError()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            var provider = new DeferredLoggerProvider(new DeferredLogSource(), testEnvironment);

            var logger = provider.CreateLogger("TestCategory");
            logger.LogInformation("Information is below the Error threshold");

            Assert.Equal(0, provider.Count);
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimesWithoutException()
        {
            var testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            var provider = new DeferredLoggerProvider(new DeferredLogSource(), testEnvironment);

            provider.Dispose();
            provider.Dispose();
        }
    }
}
