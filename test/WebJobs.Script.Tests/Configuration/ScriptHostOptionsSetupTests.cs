// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptHostOptionsSetupTests
    {
        [Fact(Skip = "Re-enable after Application Insights DI changes")]
        public void Configure_ApplicationInsightsConfig_NoSettings_CreatesDefaultSettings()
        {
            ScriptHostOptionsSetup setup = CreateSetupWithConfiguration();

            var options = new ScriptJobHostOptions();
            setup.Configure(options);

            Assert.NotNull(options.ApplicationInsightsSamplingSettings);
            Assert.Equal(5, options.ApplicationInsightsSamplingSettings.MaxTelemetryItemsPerSecond);
        }

        [Fact]
        public void Configure_FileWatching()
        {
            var settings = new Dictionary<string, string>
            {
                { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "fileWatchingEnabled"), "true" }
            };

            ScriptHostOptionsSetup setup = CreateSetupWithConfiguration(settings);

            var options = new ScriptJobHostOptions();

            // Validate default (this should be in another test - migrated here for now)
            Assert.True(options.FileWatchingEnabled);

            setup.Configure(options);

            Assert.True(options.FileWatchingEnabled);
            Assert.Equal(1, options.WatchDirectories.Count);
            Assert.Equal("node_modules", options.WatchDirectories.ElementAt(0));

            // File watching disabled
            settings[ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "fileWatchingEnabled")] = bool.FalseString;

            setup = CreateSetupWithConfiguration(settings);

            options = new ScriptJobHostOptions();
            setup.Configure(options);

            Assert.False(options.FileWatchingEnabled);

            // File watching enabled, watch directories
            settings[ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "fileWatchingEnabled")] = bool.TrueString;
            settings[ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "watchDirectories", "0")] = "Shared";
            settings[ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "watchDirectories", "1")] = "Tools";

            setup = CreateSetupWithConfiguration(settings);

            options = new ScriptJobHostOptions();
            setup.Configure(options);

            Assert.True(options.FileWatchingEnabled);
            Assert.Equal(3, options.WatchDirectories.Count);
            Assert.Equal("node_modules", options.WatchDirectories.ElementAt(0));
            Assert.Equal("Shared", options.WatchDirectories.ElementAt(1));
            Assert.Equal("Tools", options.WatchDirectories.ElementAt(2));
        }

        [Fact(Skip = "ApplyConfiguration no longer exists. Validate logic (moved to HostJsonFileConfigurationSource)")]
        public void Configure_AllowPartialHostStartup()
        {
            //var settings = new Dictionary<string, string>
            //{
            //    { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "fileWatchingEnabled"), "true" }
            //};

            //var options = new ScriptHostOptions();

            //// Validate default (this should be in another test - migrated here for now)
            //Assert.True(options.FileWatchingEnabled);

            //Assert.True(options.HostConfig.AllowPartialHostStartup);

            //// explicit setting can override our default
            //scriptConfig = new ScriptHostConfiguration();
            //config["allowPartialHostStartup"] = new JValue(true);
            //ScriptHost.ApplyConfiguration(config, scriptConfig);
            //Assert.True(scriptConfig.HostConfig.AllowPartialHostStartup);

            //// explicit setting can override our default
            //scriptConfig = new ScriptHostConfiguration();
            //config["allowPartialHostStartup"] = new JValue(false);
            //ScriptHost.ApplyConfiguration(config, scriptConfig);
            //Assert.False(scriptConfig.HostConfig.AllowPartialHostStartup);
        }

        [Fact]
        public void ConfigureLanguageWorkers_SetMaxMessageLength_DafaultIfExceedsLimit()
        {
            var settings = new Dictionary<string, string>
            {
                { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, LanguageWorkerConstants.LanguageWorkersSectionName, "maxMessageLength"), "2500" }
            };

            ScriptHostOptionsSetup setup = CreateSetupWithConfiguration(settings);

            var options = new ScriptJobHostOptions();

            setup.Configure(options);

            // TODO: Logging in setup currently disabled
            //var logMessage = loggerProvider.GetAllLogMessages().Single(l => l.FormattedMessage.StartsWith("MaxMessageLength must be between 4MB and 2000MB")).FormattedMessage;
            //Assert.Equal($"MaxMessageLength must be between 4MB and 2000MB.Default MaxMessageLength: {LanguageWorkerConstants.DefaultMaxMessageLengthBytes} will be used", logMessage);
            Assert.Equal(LanguageWorkerConstants.DefaultMaxMessageLengthBytes, options.MaxMessageLengthBytes);
        }

        [Fact]
        public void ConfigureLanguageWorkers_DefaultMaxMessageLength_IfNotDynamic()
        {
            var settings = new Dictionary<string, string>();
            var options = GetConfiguredOptions(settings);

            Assert.Equal(LanguageWorkerConstants.DefaultMaxMessageLengthBytes, options.MaxMessageLengthBytes);
        }

        [Fact]
        public void ConfigureLanguageWorkers_SetMaxMessageLength_IfNotDynamic()
        {
            var settings = new Dictionary<string, string>
            {
                { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, LanguageWorkerConstants.LanguageWorkersSectionName, "maxMessageLength"), "20" }
            };
            var options = GetConfiguredOptions(settings);

            Assert.Equal(20 * 1024 * 1024, options.MaxMessageLengthBytes);
        }

        [Fact]
        public void ConfigureLanguageWorkers_DefaultMaxMessageLength_IfDynamic()
        {
            var settings = new Dictionary<string, string>
            {
                { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, LanguageWorkerConstants.LanguageWorkersSectionName, "maxMessageLength"), "250" }
            };

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, "Dynamic");

            var options = GetConfiguredOptions(settings, environment);

            // TODO: Logger is currently disabled
            //string message = $"Cannot set {nameof(scriptConfig.MaxMessageLengthBytes)} on Consumption plan. Default MaxMessageLength: {ScriptHost.DefaultMaxMessageLengthBytesDynamicSku} will be used";
            //var logs = testLogger.GetLogMessages().Where(log => log.FormattedMessage.Contains(message)).FirstOrDefault();
            //Assert.NotNull(logs);

            Assert.Equal(LanguageWorkerConstants.DefaultMaxMessageLengthBytesDynamicSku, options.MaxMessageLengthBytes);
        }

        [Fact]
        public void ConfigureLanguageWorkers_Default_IfDynamic_NoMaxMessageLength()
        {
            var settings = new Dictionary<string, string>
            {
                { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, LanguageWorkerConstants.LanguageWorkersSectionName, "maxMessageLength"), "250" }
            };

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, "Dynamic");

            var options = GetConfiguredOptions(settings, environment);

            Assert.Equal(LanguageWorkerConstants.DefaultMaxMessageLengthBytesDynamicSku, options.MaxMessageLengthBytes);
        }

        [Fact]
        public void ConfigureLanguageWorkers_Default_IfNotDynamic_NoMaxMessageLength()
        {
            var settings = new Dictionary<string, string>();
            var options = GetConfiguredOptions(settings);

            Assert.Equal(LanguageWorkerConstants.DefaultMaxMessageLengthBytes, options.MaxMessageLengthBytes);
        }

        private ScriptJobHostOptions GetConfiguredOptions(Dictionary<string, string> settings, IEnvironment environment = null)
        {
            ScriptHostOptionsSetup setup = CreateSetupWithConfiguration(settings, environment);

            var options = new ScriptJobHostOptions();

            setup.Configure(options);

            return options;
        }

        private ScriptHostOptionsSetup CreateSetupWithConfiguration(Dictionary<string, string> settings = null, IEnvironment environment = null)
        {
            var builder = new ConfigurationBuilder();
            environment = environment ?? SystemEnvironment.Instance;

            if (settings != null)
            {
                builder.AddInMemoryCollection(settings);
            }

            var configuration = builder.Build();

            return new ScriptHostOptionsSetup(configuration, environment, new OptionsWrapper<ScriptApplicationHostOptions>(new ScriptApplicationHostOptions()));
        }
    }
}
