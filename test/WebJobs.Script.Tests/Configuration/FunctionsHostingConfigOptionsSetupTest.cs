// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class FunctionsHostingConfigOptionsSetupTest : IAsyncLifetime
    {
        private ImmutableArray<string> _systemLogCategoryPrefixesOriginalValue;

        public Task InitializeAsync()
        {
            _systemLogCategoryPrefixesOriginalValue = ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes;
            return Task.CompletedTask;
        }

        [Fact]
        public void Configure_Sets_Options()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                var environment = new TestEnvironment();
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                IConfiguration configuraton = GetConfiguration(fileName, "feature1=value1,feature2=value2", environment);
                FunctionsHostingConfigOptionsSetup setup = new FunctionsHostingConfigOptionsSetup(configuraton, environment);
                FunctionsHostingConfigOptions options = new FunctionsHostingConfigOptions();
                setup.Configure(options);

                Assert.Equal(options.GetFeature("feature1"), "value1");
            }
        }

        [Fact]
        public void Configure_DoesNotSet_Options_IfFileEmpty()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                var environment = new TestEnvironment();
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                IConfiguration configuraton = GetConfiguration(fileName, string.Empty, environment);
                FunctionsHostingConfigOptionsSetup setup = new FunctionsHostingConfigOptionsSetup(configuraton, environment);
                FunctionsHostingConfigOptions options = new FunctionsHostingConfigOptions();
                setup.Configure(options);

                Assert.Empty(options.Features);
            }
        }

        [Fact]
        public void Configure_DoesNotSet_Options_IfFileDoesNotExist()
        {
            var environment = new TestEnvironment();
            string fileName = Path.Combine("C://settings.txt");
            IConfiguration configuraton = GetConfiguration(fileName, string.Empty, environment);
            FunctionsHostingConfigOptionsSetup setup = new FunctionsHostingConfigOptionsSetup(configuraton, environment);
            FunctionsHostingConfigOptions options = new FunctionsHostingConfigOptions();
            setup.Configure(options);

            Assert.Empty(options.Features);
        }

        [Theory]
        [InlineData(true, false, true)] // RestrictHostLogs is true, FeatureFlag is not set, should result in **restricted** logs. This is the default behaviour of the host.
        [InlineData(false, true, false)] // RestrictHostLogs is false, FeatureFlag is set, should result in unrestricted logs
        [InlineData(true, true, false)] // RestrictHostLogs is true, FeatureFlag is set, should result in unrestricted logs
        [InlineData(false, false, false)] // RestrictHostLogs is false, FeatureFlag is not set, should result in unrestricted logs
        public void Configure_RestrictHostLogs_SetsSystemLogCategoryPrefixes(bool restrictHostLogs, bool setFeatureFlag, bool shouldResultInRestrictedSystemLogs)
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                var environment = new TestEnvironment();
                string fileName = Path.Combine(tempDir.Path, "settings.txt");

                if (setFeatureFlag)
                {
                    SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableHostLogs);
                }

                IConfiguration configuraton = restrictHostLogs
                    ? GetConfiguration(fileName, string.Empty, environment) // defaults to true
                    : GetConfiguration(fileName, $"{ScriptConstants.HostingConfigRestrictHostLogs}=0", environment);

                FunctionsHostingConfigOptionsSetup setup = new (configuraton, environment);
                FunctionsHostingConfigOptions options = new ();
                setup.Configure(options);

                if (shouldResultInRestrictedSystemLogs)
                {
                    Assert.Equal(ScriptConstants.RestrictedSystemLogCategoryPrefixes, ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes);
                }
                else
                {
                    Assert.Equal(ScriptConstants.SystemLogCategoryPrefixes, ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes);
                }
            }
        }

        //[Theory]
        //[InlineData(true, true, false)] // RestrictHostLogs is true, FeatureFlag is set, should result in unrestricted logs
        //public void Configure_RestrictHostLogs_LILIANTEST(bool restrictHostLogs, bool setFeatureFlag, bool shouldResultInRestrictedSystemLogs)
        //{
        //    using (TempDirectory tempDir = new TempDirectory())
        //    {
        //        var environment = new TestEnvironment();
        //        string fileName = Path.Combine(tempDir.Path, "settings.txt");

        //        if (setFeatureFlag)
        //        {
        //            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableHostLogs);
        //        }

        //        IConfiguration configuraton = restrictHostLogs
        //            ? GetConfiguration(fileName, string.Empty, environment) // defaults to true
        //            : GetConfiguration(fileName, $"{ScriptConstants.HostingConfigRestrictHostLogs}=0", environment);

        //        FunctionsHostingConfigOptionsSetup setup = new (configuraton, environment);
        //        FunctionsHostingConfigOptions options = new ();
        //        setup.Configure(options);

        //        if (shouldResultInRestrictedSystemLogs)
        //        {
        //            Assert.Equal(ScriptConstants.RestrictedSystemLogCategoryPrefixes, ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes);
        //        }
        //        else
        //        {
        //            Assert.Equal(ScriptConstants.SystemLogCategoryPrefixes, ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes);
        //        }
        //    }
        //}

        private IConfiguration GetConfiguration(string fileName, string fileContent, IEnvironment environment)
        {
            File.WriteAllText(fileName, fileContent);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsPlatformConfigFilePath, fileName);
            FunctionsHostingConfigSource source = new FunctionsHostingConfigSource(environment);
            FunctionsHostingConfigProvider provider = new FunctionsHostingConfigProvider(source);
            return new ConfigurationBuilder().Add(source).Build();
        }

        public Task DisposeAsync()
        {
            ScriptLoggingBuilderExtensions.SystemLogCategoryPrefixes = _systemLogCategoryPrefixesOriginalValue;
            return Task.CompletedTask;
        }
    }
}
