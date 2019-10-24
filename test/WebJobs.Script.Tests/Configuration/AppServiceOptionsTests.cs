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
    public class AppServiceOptionsTests
    {
        [Fact]
        public void AppServiceOptions_ReloadedOnSpecialization()
        {
            var env = new TestEnvironment();
            var token = new TestChangeTokenSource<StandbyOptions>();

            // Wire up some options.
            var host = new HostBuilder()
                .ConfigureServices(s =>
                {
                    s.AddSingleton<IEnvironment>(env);
                    s.ConfigureOptions<AppServiceOptionsSetup>();
                    s.AddSingleton<IOptionsChangeTokenSource<AppServiceOptions>, SpecializationChangeTokenSource<AppServiceOptions>>();
                    s.AddSingleton<IOptionsChangeTokenSource<StandbyOptions>>(token);
                })
                .Build();

            var options = host.Services.GetService<IOptionsMonitor<AppServiceOptions>>();
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "blah");
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName, "blah");
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteOwnerName, "blahh+1234");
            var oldUniqueSlotName = env.GetAzureWebsiteUniqueSlotName();
            var oldSubscriptionId = env.GetSubscriptionId();

            Assert.Equal(options.CurrentValue.AppName, oldUniqueSlotName);
            Assert.Equal(options.CurrentValue.SubscriptionId, oldSubscriptionId);

            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, "properblah");
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSlotName, "properblah");
            env.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteOwnerName, "properblahh+1234proper");

            // should still have the old values.
            Assert.Equal(options.CurrentValue.AppName, oldUniqueSlotName);
            Assert.Equal(options.CurrentValue.SubscriptionId, oldSubscriptionId);

            // Simulate specialization, which should refresh.
            token.SignalChange();

            Assert.Equal(options.CurrentValue.AppName, "properblah-properblah");
            Assert.Equal(options.CurrentValue.SubscriptionId, "properblahh");
        }
    }
}
