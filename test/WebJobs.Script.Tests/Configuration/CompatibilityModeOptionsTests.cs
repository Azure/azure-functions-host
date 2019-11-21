// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class CompatibilityModeOptionsTests
    {
        [Fact]
        public void CompatibilityMode_ReloadedOnSpecialization()
        {
            var env = new TestEnvironment();
            var token = new TestChangeTokenSource<StandbyOptions>();

            // Wire up some options.
            var host = new HostBuilder()
                .ConfigureServices(s =>
                {
                    s.AddSingleton<IEnvironment>(env);
                    s.ConfigureOptions<CompatibilityModeOptionsSetup>();
                    s.AddSingleton<IOptionsChangeTokenSource<CompatibilityModeOptions>, SpecializationChangeTokenSource<CompatibilityModeOptions>>();
                    s.AddSingleton<IOptionsChangeTokenSource<StandbyOptions>>(token);
                })
                .Build();

            var options = host.Services.GetService<IOptionsMonitor<CompatibilityModeOptions>>();
            Assert.False(options.CurrentValue.IsV2CompatibilityModeEnabled);

            env.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey, "true");

            // should still be false.
            Assert.False(options.CurrentValue.IsV2CompatibilityModeEnabled);

            // Simulate specialization, which should refresh.
            token.SignalChange();

            Assert.True(options.CurrentValue.IsV2CompatibilityModeEnabled);
        }
    }
}
