// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class HttpBodyControlOptionsTests
    {
        [Fact]
        public void ChangeToken_ResetsValues()
        {
            // Use the production setup for services to ensure all options are wired up correctly.
            var services = new ServiceCollection();
            var startup = new Startup(null);
            startup.ConfigureServices(services);

            // Allow us to signal change.
            var standbyChangeTokenSource = new TestChangeTokenSource<StandbyOptions>();
            services.AddSingleton<IOptionsChangeTokenSource<StandbyOptions>>(_ => standbyChangeTokenSource);

            // Allow us to override environment
            var environment = new TestEnvironment();
            services.AddSingleton<IEnvironment>(_ => environment);

            var serviceProvider = services.BuildServiceProvider();
            var bodyControlOptions = serviceProvider.GetService<IOptionsMonitor<HttpBodyControlOptions>>();

            // Verify default
            Assert.False(bodyControlOptions.CurrentValue.AllowSynchronousIO);

            // Verify FeatureFlag
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagAllowSynchronousIO);
            Assert.False(bodyControlOptions.CurrentValue.AllowSynchronousIO);
            standbyChangeTokenSource.SignalChange();
            Assert.True(bodyControlOptions.CurrentValue.AllowSynchronousIO);

            // Set FeatureFlag and CompatMode
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey, "true");
            standbyChangeTokenSource.SignalChange();
            Assert.True(bodyControlOptions.CurrentValue.AllowSynchronousIO);

            // Clear all
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey, null);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, null);
            Assert.True(bodyControlOptions.CurrentValue.AllowSynchronousIO);
            standbyChangeTokenSource.SignalChange();
            Assert.False(bodyControlOptions.CurrentValue.AllowSynchronousIO);

            // Set CompatMode
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey, "true");
            Assert.False(bodyControlOptions.CurrentValue.AllowSynchronousIO);
            standbyChangeTokenSource.SignalChange();
            Assert.True(bodyControlOptions.CurrentValue.AllowSynchronousIO);
        }
    }
}
