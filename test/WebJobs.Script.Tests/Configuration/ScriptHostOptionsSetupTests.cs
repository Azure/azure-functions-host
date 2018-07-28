// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScriptHostOptionsSetupTests
    {
        [Fact]
        public void Configure_ApplicationInsightsConfig_NoSettings_CreatesDefaultSettings()
        {
            ScriptHostOptionsSetup setup = CreateSetupWithConfiguration();

            var options = new ScriptHostOptions();
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

            var options = new ScriptHostOptions();

            // Validate default (this should be in another test - migrated here for now)
            Assert.True(options.FileWatchingEnabled);

            setup.Configure(options);

            Assert.True(options.FileWatchingEnabled);
            Assert.Equal(1, options.WatchDirectories.Count);
            Assert.Equal("node_modules", options.WatchDirectories.ElementAt(0));

            // File watching disabled
            settings[ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "fileWatchingEnabled")] = bool.FalseString;

            setup = CreateSetupWithConfiguration(settings);

            options = new ScriptHostOptions();
            setup.Configure(options);

            Assert.False(options.FileWatchingEnabled);

            // File watching enabled, watch directories
            settings[ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "fileWatchingEnabled")] = bool.TrueString;
            settings[ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "watchDirectories", "0")] = "Shared";
            settings[ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "watchDirectories", "1")] = "Tools";

            setup = CreateSetupWithConfiguration(settings);

            options = new ScriptHostOptions();
            setup.Configure(options);

            Assert.True(options.FileWatchingEnabled);
            Assert.Equal(3, options.WatchDirectories.Count);
            Assert.Equal("node_modules", options.WatchDirectories.ElementAt(0));
            Assert.Equal("Shared", options.WatchDirectories.ElementAt(1));
            Assert.Equal("Tools", options.WatchDirectories.ElementAt(2));
        }

        private ScriptHostOptionsSetup CreateSetupWithConfiguration(Dictionary<string, string> settings = null)
        {
            var builder = new ConfigurationBuilder();

            if (settings != null)
            {
                builder.AddInMemoryCollection(settings);
            }

            var configuration = builder.Build();

            return new ScriptHostOptionsSetup(configuration, new OptionsWrapper<ScriptWebHostOptions>(new ScriptWebHostOptions()));
        }
    }
}
