// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ConsoleLoggingOptionsSetupTests
    {
        [Fact]
        public void ConsoleLoggingOptionsSetup_ConfiguresExpectedDefaults()
        {
            IConfiguration config = new ConfigurationBuilder()
               .AddInMemoryCollection(new Dictionary<string, string>
               {
                   //{ $"{SamplingSettings}:MaxTelemetryItemsPerSecond", "25" },
                   //{ $"{SamplingSettings}:IsEnabled", "false" }
               })
               .Build();

            ConsoleLoggingOptionsSetup setup = new ConsoleLoggingOptionsSetup(config);
            ConsoleLoggingOptions options = new ConsoleLoggingOptions();

            setup.Configure(options);

            Assert.Equal(true, options.BufferEnabled);
            Assert.Equal(false, options.LoggingDisabled);
            Assert.Equal(8000, options.BufferSize);
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("0", false)]
        [InlineData(null, false)]
        public void ConsoleLoggingOptionsSetup_CanDisableLogging(string value, bool expectLoggingDisabled)
        {
            var settings = new Dictionary<string, string>();

            if (value != null)
            {
                settings[EnvironmentSettingNames.ConsoleLoggingDisabled] = value;
            }

            IConfiguration config = new ConfigurationBuilder()
               .AddInMemoryCollection(settings)
               .Build();

            ConsoleLoggingOptionsSetup setup = new ConsoleLoggingOptionsSetup(config);
            ConsoleLoggingOptions options = new ConsoleLoggingOptions();
            setup.Configure(options);

            Assert.Equal(expectLoggingDisabled, options.LoggingDisabled);
        }

        [Fact]
        public void ConsoleLoggingOptionsSetup_CanDisableBuffer()
        {
            var settings = new Dictionary<string, string>();
            settings[EnvironmentSettingNames.ConsoleLoggingBufferSize] = "0";

            IConfiguration config = new ConfigurationBuilder()
               .AddInMemoryCollection(settings)
               .Build();

            ConsoleLoggingOptionsSetup setup = new ConsoleLoggingOptionsSetup(config);
            ConsoleLoggingOptions options = new ConsoleLoggingOptions();
            setup.Configure(options);

            Assert.Equal(false, options.BufferEnabled);
        }

        [Fact]
        public void ConsoleLoggingOptionsSetup_CanSetBufferSize()
        {
            var settings = new Dictionary<string, string>();
            settings[EnvironmentSettingNames.ConsoleLoggingBufferSize] = "100";

            IConfiguration config = new ConfigurationBuilder()
               .AddInMemoryCollection(settings)
               .Build();

            ConsoleLoggingOptionsSetup setup = new ConsoleLoggingOptionsSetup(config);
            ConsoleLoggingOptions options = new ConsoleLoggingOptions();
            setup.Configure(options);

            Assert.Equal(100, options.BufferSize);
        }

        [Fact]
        public void ConsoleLoggingOptionsSetup_DoesNotOverwriteCustomBufferSizeIfNotSet()
        {
            var settings = new Dictionary<string, string>();

            IConfiguration config = new ConfigurationBuilder()
               .AddInMemoryCollection(settings)
               .Build();

            ConsoleLoggingOptionsSetup setup = new ConsoleLoggingOptionsSetup(config);
            ConsoleLoggingOptions options = new ConsoleLoggingOptions { BufferSize = 100 };
            setup.Configure(options);

            Assert.Equal(100, options.BufferSize);
        }
    }
}
