// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    // Tests for configuration that lives under the "Logging" section of the IConfiguration
    public class LoggingConfigurationTests
    {
        private readonly string _loggingPath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "Logging");

        [Fact]
        public void Logging_Binds_AppInsightsOptions()
        {
            IHost host = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "APPINSIGHTS_INSTRUMENTATIONKEY", "some_key" },
                        { ConfigurationPath.Combine(_loggingPath, "ApplicationInsights", "SamplingSettings", "IsEnabled"), "false" },
                        { ConfigurationPath.Combine(_loggingPath, "ApplicationInsights", "SnapshotConfiguration", "IsEnabled"), "false" }
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            ApplicationInsightsLoggerOptions appInsightsOptions = host.Services.GetService<IOptions<ApplicationInsightsLoggerOptions>>().Value;

            Assert.Equal("some_key", appInsightsOptions.InstrumentationKey);
            Assert.Null(appInsightsOptions.SamplingSettings);
            Assert.False(appInsightsOptions.SnapshotConfiguration.IsEnabled);
        }

        [Fact]
        public void Logging_Filters()
        {
            // Ensure that the logging path is configured for filter configuration binding in .NET Core.
            // All of the filtering details are handled by the built-in logging infrastructure, so here we
            // simply want to make sure that we're populating the LoggerFilterOptions from our config.

            IHost host = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { ConfigurationPath.Combine(_loggingPath, "LogLevel", "Default"), "Error" },
                        { ConfigurationPath.Combine(_loggingPath, "LogLevel", "Some.Custom.Category"), "Trace" },
                        { ConfigurationPath.Combine(_loggingPath, "Console", "LogLevel", "Default"), "Trace" }
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            LoggerFilterOptions filterOptions = host.Services.GetService<IOptions<LoggerFilterOptions>>().Value;

            Assert.Equal(4, filterOptions.Rules.Count);

            Assert.Equal(LogLevel.Trace, filterOptions.Rules[0].LogLevel);
            Assert.Equal("Console", filterOptions.Rules[0].ProviderName);

            Assert.Equal(LogLevel.Trace, filterOptions.Rules[1].LogLevel);
            Assert.Null(filterOptions.Rules[1].ProviderName);
            Assert.Equal("Some.Custom.Category", filterOptions.Rules[1].CategoryName);

            Assert.Equal(LogLevel.Error, filterOptions.Rules[2].LogLevel);
            Assert.Null(filterOptions.Rules[2].ProviderName);

            Assert.Equal(typeof(SystemLoggerProvider).FullName, filterOptions.Rules[3].ProviderName);
            Assert.Equal(LogLevel.Debug, filterOptions.Rules[3].LogLevel);
            Assert.Null(filterOptions.Rules[3].CategoryName);
            Assert.Null(filterOptions.Rules[3].Filter);
        }

        [Fact]
        public void Logging_DefaultsToInformation()
        {
            IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            LoggerFilterOptions filterOptions = host.Services.GetService<IOptions<LoggerFilterOptions>>().Value;

            Assert.Equal(LogLevel.Information, filterOptions.MinLevel);
            LoggerFilterRule rule = filterOptions.Rules.Single();
            Assert.Equal(typeof(SystemLoggerProvider).FullName, rule.ProviderName);
            Assert.Equal(LogLevel.Debug, rule.LogLevel);
            Assert.Null(rule.CategoryName);
            Assert.Null(rule.Filter);
        }

        [Fact]
        public void Initialize_AppliesLoggerConfig()
        {
            // Default mininum level is Information, so set this to Warning and make
            // sure Information-level logs are filtered

            TestLoggerProvider loggerProvider = new TestLoggerProvider();
            IHost host = new HostBuilder()
                .ConfigureAppConfiguration(c =>
                {
                    c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { ConfigurationPath.Combine(_loggingPath, "LogLevel", "Default"), "Warning" },
                    });
                })
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureLogging(l =>
                {
                    l.AddProvider(loggerProvider);
                })
                .Build();

            ILogger logger = host.Services.GetService<ILogger<LoggingConfigurationTests>>();

            logger.LogInformation("Information");
            logger.LogWarning("Warning");

            LogMessage messages = loggerProvider.GetAllLogMessages().Single();
            Assert.Equal("Warning", messages.FormattedMessage);
        }

        [Fact]
        public void LoggerProviders_Default()
        {
            IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            IEnumerable<ILoggerProvider> loggerProviders = host.Services.GetService<IEnumerable<ILoggerProvider>>();

            Assert.Equal(3, loggerProviders.Count());
            loggerProviders.OfType<SystemLoggerProvider>().Single();
            loggerProviders.OfType<HostFileLoggerProvider>().Single();
            loggerProviders.OfType<FunctionFileLoggerProvider>().Single();
        }

        [Fact]
        public void LoggerProviders_ConsoleEnabled_IfDevelopmentEnvironment()
        {
            IHost host = new HostBuilder()
                .UseEnvironment("Development")
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            IEnumerable<ILoggerProvider> loggerProviders = host.Services.GetService<IEnumerable<ILoggerProvider>>();

            Assert.Equal(4, loggerProviders.Count());
            loggerProviders.OfType<SystemLoggerProvider>().Single();
            loggerProviders.OfType<HostFileLoggerProvider>().Single();
            loggerProviders.OfType<FunctionFileLoggerProvider>().Single();
            loggerProviders.OfType<ConsoleLoggerProvider>().Single();
        }

        [Fact]
        public void LoggerProviders_ConsoleEnabled_InConfiguration()
        {
            IHost host = new HostBuilder()
                 .ConfigureAppConfiguration(c =>
                 {
                     c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { ConfigurationPath.Combine(_loggingPath, "Console", "IsEnabled"), "True" }
                    });
                 })
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            IEnumerable<ILoggerProvider> loggerProviders = host.Services.GetService<IEnumerable<ILoggerProvider>>();

            Assert.Equal(4, loggerProviders.Count());
            loggerProviders.OfType<SystemLoggerProvider>().Single();
            loggerProviders.OfType<HostFileLoggerProvider>().Single();
            loggerProviders.OfType<FunctionFileLoggerProvider>().Single();
            loggerProviders.OfType<ConsoleLoggerProvider>().Single();
        }

        [Fact]
        public void LoggerProviders_ApplicationInsights()
        {
            IHost host = new HostBuilder()
               .ConfigureAppConfiguration(c =>
               {
                   c.AddInMemoryCollection(new Dictionary<string, string>
                  {
                        { "APPINSIGHTS_INSTRUMENTATIONKEY", "some_key" }
                  });
               })
              .ConfigureDefaultTestWebScriptHost()
              .Build();

            IEnumerable<ILoggerProvider> loggerProviders = host.Services.GetService<IEnumerable<ILoggerProvider>>();

            Assert.Equal(4, loggerProviders.Count());
            loggerProviders.OfType<SystemLoggerProvider>().Single();
            loggerProviders.OfType<HostFileLoggerProvider>().Single();
            loggerProviders.OfType<FunctionFileLoggerProvider>().Single();
            loggerProviders.OfType<ApplicationInsightsLoggerProvider>().Single();
        }
    }
}