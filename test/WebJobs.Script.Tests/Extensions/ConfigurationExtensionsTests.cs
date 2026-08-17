// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class ConfigurationExtensionsTests
    {
        [Theory]
        [InlineData("1", true)]
        [InlineData("0", false)]
        public void IsPlaceholderModeEnabled_WithConfiguredValue_ReturnsExpectedResult(
            string value, bool expected)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [EnvironmentSettingNames.AzureWebsitePlaceholderMode] = value,
                })
                .Build();

            bool result = configuration.IsPlaceholderModeEnabled();

            result.Should().Be(expected);
        }

        [Fact]
        public void IsPlaceholderModeEnabled_WithMissingValue_ReturnsFalse()
        {
            IConfiguration configuration = new ConfigurationBuilder().Build();

            bool result = configuration.IsPlaceholderModeEnabled();

            result.Should().BeFalse();
        }

        [Fact]
        public void IsPlaceholderModeEnabled_WithNullConfiguration_ThrowsArgumentNullException()
        {
            IConfiguration configuration = null;

            Action act = () => configuration.IsPlaceholderModeEnabled();

            act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
        }
    }
}
