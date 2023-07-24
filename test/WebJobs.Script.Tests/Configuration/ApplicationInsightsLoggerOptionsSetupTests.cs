// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ApplicationInsightsLoggerOptionsSetupTests
    {
        private const string SamplingSettings = nameof(ApplicationInsightsLoggerOptions.SamplingSettings);
        private const string SnapshotConfiguration = nameof(ApplicationInsightsLoggerOptions.SnapshotConfiguration);

        private readonly TestEnvironment _environment = new TestEnvironment();

        [Fact]
        public void Configure_SamplingDisabled_CreatesNullSettings()
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { $"{SamplingSettings}:IsEnabled", "false" }
                })
                .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Null(options.SamplingSettings);
        }

        [Fact]
        public void Configure_SamplingDisabled_IgnoresOtherSettings()
        {
            IConfiguration config = new ConfigurationBuilder()
               .AddInMemoryCollection(new Dictionary<string, string>
               {
                    { $"{SamplingSettings}:MaxTelemetryItemsPerSecond", "25" },
                    { $"{SamplingSettings}:IsEnabled", "false" }
               })
               .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Null(options.SamplingSettings);
            Assert.Null(options.SamplingExcludedTypes);
            Assert.Null(options.SamplingIncludedTypes);
        }

        [Fact]
        public void Configure_SamplingExcludedIncludedTypes_AppliesSettings()
        {
            string excludedTypes = "Dependency;Event";
            string includedTypes = "PageView;Trace";

            IConfiguration config = new ConfigurationBuilder()
               .AddInMemoryCollection(new Dictionary<string, string>
               {
                   { $"{SamplingSettings}:MaxTelemetryItemsPerSecond", "25" },
                   { $"{SamplingSettings}:IsEnabled", "true" },
                   { $"{SamplingSettings}:ExcludedTypes", excludedTypes },
                   { $"{SamplingSettings}:IncludedTypes", includedTypes },
               })
               .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Equal(excludedTypes, options.SamplingExcludedTypes);
            Assert.Equal(includedTypes, options.SamplingIncludedTypes);
        }

        [Fact]
        public void Configure_SnapshotEnabled_CreatesDefaultSettings()
        {
            IConfiguration config = new ConfigurationBuilder()
              .AddInMemoryCollection(new Dictionary<string, string>
              {
                    { $"{SnapshotConfiguration}:IsEnabled", "true" }
              })
              .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.True(options.SnapshotConfiguration.IsEnabled);
        }

        [Fact]
        public void Configure_SnapshotDisabled_ByDefault()
        {
            IConfiguration config = new ConfigurationBuilder().Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Null(options.SnapshotConfiguration);
        }

        [Fact]
        public void Configure_Snapshot_Sets_SnapshotsPerTenMinutesLimit()
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { $"{SnapshotConfiguration}:SnapshotsPerTenMinutesLimit", "10" },
                })
                .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Equal(10, options.SnapshotConfiguration.SnapshotsPerTenMinutesLimit);
        }

        [Fact]
        public void Configure_SnapshotDisabled_SetsIsEnabledFalse()
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { $"{SnapshotConfiguration}:IsEnabled", "false" }
                })
                .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.False(options.SnapshotConfiguration.IsEnabled);
        }

        [Fact]
        public void Configure_SamplingEnabled_CreatesDefaultSettings()
        {
            IConfiguration config = new ConfigurationBuilder()
              .AddInMemoryCollection(new Dictionary<string, string>
              {
                    { $"{SamplingSettings}:IsEnabled", "true" }
              })
              .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Equal(20, options.SamplingSettings.MaxTelemetryItemsPerSecond);
        }

        [Fact]
        public void Configure_SamplingEnabled_ByDefault()
        {
            IConfiguration config = new ConfigurationBuilder().Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Equal(20, options.SamplingSettings.MaxTelemetryItemsPerSecond);
        }

        [Fact]
        public void Configure_Sampling_Sets_MaxTelemetryItemsPerSecond()
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { $"{SamplingSettings}:MaxTelemetryItemsPerSecond", "100" },
                })
                .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Equal(100, options.SamplingSettings.MaxTelemetryItemsPerSecond);
        }

        [Fact]
        public void Configure_Sampling_Property_Initializes_MaxTelemetryItemsPerSecond()
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { $"{SamplingSettings}:MaxSamplingPercentage", "50" },
                })
                .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Equal(50, options.SamplingSettings.MaxSamplingPercentage);
            Assert.Equal(20, options.SamplingSettings.MaxTelemetryItemsPerSecond);
        }

        [Fact]
        public void Configure_QuickPulse()
        {
            IConfiguration config = new ConfigurationBuilder().Build();

            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AppInsightsQuickPulseAuthApiKey, "SOME_API_KEY");

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Equal("SOME_API_KEY", options.QuickPulseAuthenticationApiKey);
        }

        [Fact]
        public void Configure_QuickPulse_NullByDefault()
        {
            IConfiguration config = new ConfigurationBuilder().Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Null(options.QuickPulseAuthenticationApiKey);
        }

        [Theory]
        [InlineData(null, true)] // default value
        [InlineData("true", true)]
        [InlineData("false", false)]
        public void Configure_EnableLiveMetrics(string value, bool expectedValue)
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "EnableLiveMetrics", value },
                })
                .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Equal(expectedValue, options.EnableLiveMetrics);
        }

        [Theory]
        [InlineData(null, false)] // default value
        [InlineData("true", true)]
        [InlineData("false", false)]
        public void Configure_EnableLiveMetricsFilters(string value, bool expectedValue)
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "EnableLiveMetricsFilters", value },
                })
                .Build();

            ApplicationInsightsLoggerOptionsSetup setup = new ApplicationInsightsLoggerOptionsSetup(new MockLoggerConfiguration(config), _environment);

            ApplicationInsightsLoggerOptions options = new ApplicationInsightsLoggerOptions();
            setup.Configure(options);

            Assert.Equal(expectedValue, options.EnableLiveMetricsFilters);
        }

        private class MockLoggerConfiguration : ILoggerProviderConfiguration<ApplicationInsightsLoggerProvider>
        {
            public MockLoggerConfiguration(IConfiguration configuration)
            {
                Configuration = configuration;
            }

            public IConfiguration Configuration { get; }
        }
    }
}
