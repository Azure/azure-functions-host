// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ConsoleLoggingOptionsSetupTests
    {
        [Fact]
        public void ConsoleLoggingOptionsSetup_ConfiguresExpectedDefaults()
        {
            MutableTestConfiguration configuration = new();

            ConsoleLoggingOptionsSetup setup = new(configuration.Configuration);
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
            MutableTestConfiguration configuration = new();
            if (value is not null)
            {
                configuration.Set(EnvironmentSettingNames.ConsoleLoggingDisabled, value);
                configuration.Reload();
            }

            ConsoleLoggingOptionsSetup setup = new(configuration.Configuration);
            ConsoleLoggingOptions options = new ConsoleLoggingOptions();
            setup.Configure(options);

            Assert.Equal(expectLoggingDisabled, options.LoggingDisabled);
        }

        [Fact]
        public void ConsoleLoggingOptionsSetup_CanDisableBuffer()
        {
            MutableTestConfiguration configuration = new();
            configuration.Set(EnvironmentSettingNames.ConsoleLoggingBufferSize, "0");
            configuration.Reload();

            ConsoleLoggingOptionsSetup setup = new(configuration.Configuration);
            ConsoleLoggingOptions options = new ConsoleLoggingOptions();
            setup.Configure(options);

            Assert.Equal(false, options.BufferEnabled);
        }

        [Fact]
        public void ConsoleLoggingOptionsSetup_CanSetBufferSize()
        {
            MutableTestConfiguration configuration = new();
            configuration.Set(EnvironmentSettingNames.ConsoleLoggingBufferSize, "100");
            configuration.Reload();

            ConsoleLoggingOptionsSetup setup = new(configuration.Configuration);
            ConsoleLoggingOptions options = new ConsoleLoggingOptions();
            setup.Configure(options);

            Assert.Equal(100, options.BufferSize);
        }

        [Fact]
        public void ConsoleLoggingOptionsSetup_DoesNotOverwriteCustomBufferSizeIfNotSet()
        {
            MutableTestConfiguration configuration = new();

            ConsoleLoggingOptionsSetup setup = new(configuration.Configuration);
            ConsoleLoggingOptions options = new ConsoleLoggingOptions { BufferSize = 100 };
            setup.Configure(options);

            Assert.Equal(100, options.BufferSize);
        }
    }
}
