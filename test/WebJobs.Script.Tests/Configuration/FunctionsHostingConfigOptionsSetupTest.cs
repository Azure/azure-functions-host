// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class FunctionsHostingConfigOptionsSetupTest
    {
        [Fact]
        public void Configure_Sets_Options()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                IConfiguration configuraton = GetConfiguration(fileName, $"feature1=value1,feature2=value2");
                FunctionsHostingConfigOptionsSetup setup = new FunctionsHostingConfigOptionsSetup(configuraton);
                Config.FunctionsHostingConfigOptions options = new Config.FunctionsHostingConfigOptions();
                setup.Configure(options);

                Assert.Equal(options.GetFeature("feature1"), "value1");
            }
        }

        [Fact]
        public void Configure_DoesNotSet_Options_IfFileEmpty()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                IConfiguration configuraton = GetConfiguration(fileName, string.Empty);
                FunctionsHostingConfigOptionsSetup setup = new FunctionsHostingConfigOptionsSetup(configuraton);
                Config.FunctionsHostingConfigOptions options = new Config.FunctionsHostingConfigOptions();
                setup.Configure(options);

                Assert.Empty(options.Features);
            }
        }

        [Fact]
        public void Configure_DoesNotSet_Options_IfFileDoesNotExist()
        {
            string fileName = Path.Combine("C://settings.txt");
            IConfiguration configuraton = GetConfiguration(fileName, string.Empty);
            FunctionsHostingConfigOptionsSetup setup = new FunctionsHostingConfigOptionsSetup(configuraton);
            FunctionsHostingConfigOptions options = new FunctionsHostingConfigOptions();
            setup.Configure(options);

            Assert.Empty(options.Features);
        }

        [Theory]
        [InlineData(true, false, true)] // RestrictHostLogs is true, FeatureFlag is not set, should result in **restricted** logs. This is the default behaviour of the host.
        [InlineData(false, true, false)] // RestrictHostLogs is false, FeatureFlag is set, should result in unrestricted logs
        // [InlineData(true, true, false)] // RestrictHostLogs is true, FeatureFlag is set, should result in unrestricted logs
        [InlineData(false, false, false)] // RestrictHostLogs is false, FeatureFlag is not set, should result in unrestricted logs
        public void Configure_RestrictHostLogs_SetsSystemLogCategoryPrefixes(bool restrictHostLogs, bool setFeatureFlag, bool shouldResultInRestrictedSystemLogs)
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");

                IConfiguration configuraton = restrictHostLogs
                                    ? GetConfiguration(fileName, string.Empty) // defaults to true
                                    : GetConfiguration(fileName, $"{ScriptConstants.HostingConfigRestrictHostLogs}=0");

                try
                {
                    if (setFeatureFlag)
                    {
                        SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableHostLogs);
                    }

                    // Log environment variable
                    Console.WriteLine($"FeatureFlag: {SystemEnvironment.Instance.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags)}");

                    FunctionsHostingConfigOptionsSetup setup = new (configuraton);
                    FunctionsHostingConfigOptions options = new ();
                    setup.Configure(options);

                    Console.WriteLine($"RestrictHostLogs: {options.RestrictHostLogs}");

                    // Log the result
                    Console.WriteLine($"SystemLogCategoryPrefixes: {ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes}");

                    // Assert
                    if (shouldResultInRestrictedSystemLogs)
                    {
                        Assert.Equal(ScriptConstants.RestrictedSystemLogCategoryPrefixes, ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes);
                    }
                    else
                    {
                        Assert.Equal(ScriptConstants.SystemLogCategoryPrefixes, ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes);
                    }
                }
                finally
                {
                    // Reset to default values
                    SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, null);
                    ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes = ScriptConstants.SystemLogCategoryPrefixes;
                }
            }
        }

        [Theory]
        [InlineData(true, true, false)] // RestrictHostLogs is true, FeatureFlag is set, should result in unrestricted logs
        public void Configure_RestrictHostLogs_LILIANTEST(bool restrictHostLogs, bool setFeatureFlag, bool shouldResultInRestrictedSystemLogs)
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");

                try
                {
                    if (setFeatureFlag)
                    {
                        SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableHostLogs);
                    }

                    IConfiguration configuraton = restrictHostLogs
                        ? GetConfiguration(fileName, string.Empty) // defaults to true
                        : GetConfiguration(fileName, $"{ScriptConstants.HostingConfigRestrictHostLogs}=0");

                    // Log environment variable
                    Console.WriteLine($"FeatureFlag: {SystemEnvironment.Instance.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags)}");

                    FunctionsHostingConfigOptionsSetup setup = new (configuraton);
                    FunctionsHostingConfigOptions options = new ();
                    setup.Configure(options);

                    Console.WriteLine($"RestrictHostLogs: {options.RestrictHostLogs}");

                    // Log the result
                    Console.WriteLine($"SystemLogCategoryPrefixes: {ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes}");

                    // Assert
                    if (shouldResultInRestrictedSystemLogs)
                    {
                        Assert.Equal(ScriptConstants.RestrictedSystemLogCategoryPrefixes, ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes);
                    }
                    else
                    {
                        Assert.Equal(ScriptConstants.SystemLogCategoryPrefixes, ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes);
                    }
                }
                finally
                {
                    // Reset to default values
                    SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, null);
                    ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes = ScriptConstants.SystemLogCategoryPrefixes;
                }
            }
        }

        private IConfiguration GetConfiguration(string fileName, string fileContent)
        {
            File.WriteAllText(fileName, fileContent);
            Mock<IEnvironment> mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(x => x.GetEnvironmentVariable(It.Is<string>(x => x == EnvironmentSettingNames.FunctionsPlatformConfigFilePath))).Returns(fileName);
            FunctionsHostingConfigSource source = new FunctionsHostingConfigSource(mockEnvironment.Object);
            FunctionsHostingConfigProvider provider = new FunctionsHostingConfigProvider(source);
            return new ConfigurationBuilder().Add(source).Build();
        }
    }
}
