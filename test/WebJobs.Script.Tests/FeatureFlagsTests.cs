// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Config;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FeatureFlagsTests
    {
        public FeatureFlagsTests()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "AwesomeFeature,RadFeature");
        }

        [Theory]
        [InlineData("AwesomeFeature", true)]
        [InlineData("AWESOMEFEATURE", true)]
        [InlineData("radfeature", true)]
        [InlineData("brokenfeature", false)]
        public void IsEnabled_ReturnsExpectedValue(string name, bool expected)
        {
            Assert.Equal(FeatureFlags.IsEnabled(name), expected);
        }
    }
}
