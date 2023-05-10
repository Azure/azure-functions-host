// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FeatureFlagsTests
    {
        public FeatureFlagsTests()
        {
            FeatureFlags.InternalCache = null;
        }

        [Theory]
        [InlineData("AwesomeFeature", true)]
        [InlineData("AWESOMEFEATURE", true)]
        [InlineData("radfeature", true)]
        [InlineData("brokenfeature", false)]
        public void IsEnabled_ReturnsExpectedValue_AndCaches(string name, bool expected)
        {
            var env = new TestEnvironment();
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "AwesomeFeature,RadFeature");

            Assert.Equal(FeatureFlags.IsEnabled(name, env), expected);
            Assert.Collection(FeatureFlags.InternalCache,
                p => Assert.Equal("AwesomeFeature", p),
                p => Assert.Equal("RadFeature", p));

            // change the feature flag and verify it is caching the values and not re-evaluating
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "VeryNewFeature");
            Assert.Equal(FeatureFlags.IsEnabled(name, env), expected);
            Assert.False(FeatureFlags.IsEnabled("VeryNewFeature", env));
            Assert.Collection(FeatureFlags.InternalCache,
                p => Assert.Equal("AwesomeFeature", p),
                p => Assert.Equal("RadFeature", p));
        }

        [Theory]
        [InlineData("AwesomeFeature", true)]
        [InlineData("AWESOMEFEATURE", true)]
        [InlineData("radfeature", true)]
        [InlineData("brokenfeature", false)]
        public void IsEnabled_Placeholder_ReturnsExpectedValue_AndDoesNotCache(string name, bool expected)
        {
            var env = new TestEnvironment();
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "AwesomeFeature,RadFeature");
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            Assert.Equal(FeatureFlags.IsEnabled(name, env), expected);
            Assert.Null(FeatureFlags.InternalCache);

            // change the feature flag and verify it's not caching the values
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, "VeryNewFeature");
            Assert.False(FeatureFlags.IsEnabled(name, env));
            Assert.True(FeatureFlags.IsEnabled("VeryNewFeature", env));
            Assert.Null(FeatureFlags.InternalCache);
        }
    }
}
