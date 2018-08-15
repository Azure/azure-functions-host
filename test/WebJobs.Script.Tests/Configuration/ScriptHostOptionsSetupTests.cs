// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptHostOptionsSetupTests
    {
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
        public void Configure_AppliesDefaults_IfDynamic()
        {
            var settings = new Dictionary<string, string>
            {
                { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, LanguageWorkerConstants.LanguageWorkersSectionName, "maxMessageLength"), "2500" }
            };

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, "Dynamic");

            var options = GetConfiguredOptions(settings, environment);

            Assert.Equal(ScriptHostOptionsSetup.DefaultFunctionTimeout, options.FunctionTimeout);
            Assert.Equal(LanguageWorkerConstants.DefaultMaxMessageLengthBytesDynamicSku, options.MaxMessageLengthBytes);

            // TODO: DI Need to ensure JobHostOptions is correctly configured
            //var timeoutConfig = options.HostOptions.FunctionTimeout;
            //Assert.NotNull(timeoutConfig);
            //Assert.True(timeoutConfig.ThrowOnTimeout);
            //Assert.Equal(scriptConfig.FunctionTimeout.Value, timeoutConfig.Timeout);
        }

        [Fact]
        public void Configure_AppliesTimeout()
        {
            var settings = new Dictionary<string, string>
            {
                { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "functionTimeout"), "00:00:30" }
            };

            var options = GetConfiguredOptions(settings);

            Assert.Equal(TimeSpan.FromSeconds(30), options.FunctionTimeout);
        }

        [Fact]
        public void Configure_TimeoutDefaultsNull_IfNotDynamic()
        {
            var options = GetConfiguredOptions(new Dictionary<string, string>());
            Assert.Null(options.FunctionTimeout);
        }

        [Fact]
        public void Configure_NoTimeoutLimits_IfNotDynamic()
        {
            var timeout = ScriptHostOptionsSetup.MaxFunctionTimeout + TimeSpan.FromSeconds(1);
            string configPath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "functionTimeout");
            var settings = new Dictionary<string, string>
            {
                { configPath, timeout.ToString() }
            };

            var options = GetConfiguredOptions(settings);
            Assert.Equal(timeout, options.FunctionTimeout);

            timeout = ScriptHostOptionsSetup.MinFunctionTimeout - TimeSpan.FromSeconds(1);
            settings[configPath] = timeout.ToString();
            options = GetConfiguredOptions(settings);
            Assert.Equal(timeout, options.FunctionTimeout);
        }

        [Fact]
        public void Configure_AppliesTimeoutLimits_IfDynamic()
        {
            string configPath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "functionTimeout");
            var settings = new Dictionary<string, string>
            {
                { configPath, (ScriptHostOptionsSetup.MaxFunctionTimeout + TimeSpan.FromSeconds(1)).ToString() }
            };

            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, "Dynamic");

            var ex = Assert.Throws<ArgumentException>(() => GetConfiguredOptions(settings, environment));
            var expectedMessage = "FunctionTimeout must be between 00:00:01 and 00:10:00.";
            Assert.Equal(expectedMessage, ex.Message);

            settings[configPath] = (ScriptHostOptionsSetup.MinFunctionTimeout - TimeSpan.FromSeconds(1)).ToString();
            Assert.Throws<ArgumentException>(() => GetConfiguredOptions(settings, environment));
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Fact]
        public void ConfigureLanguageWorkers_SetMaxMessageLength_DafaultIfExceedsLimit()
        {
            var settings = new Dictionary<string, string>
            {
                { ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, LanguageWorkerConstants.LanguageWorkersSectionName, "maxMessageLength"), "2500" }
            };

            var options = GetConfiguredOptions(settings);

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
