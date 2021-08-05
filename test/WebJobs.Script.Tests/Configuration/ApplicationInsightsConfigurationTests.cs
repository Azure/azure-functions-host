// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ApplicationInsightsConfigurationTests
    {
        [Fact]
        public void ServicesDisabled_InPlaceholderMode()
        {
            IHost host;
            using (new TestScopedEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1"))
            {
                host = new HostBuilder()
                    .ConfigureServices(s =>
                    {
                        s.AddSingleton<IEnvironment>(SystemEnvironment.Instance);
                    })
                    .Build();
            }

            // No DependencyTrackingTelemetryModule should be registered
            var modules = host.Services.GetService<IEnumerable<ITelemetryModule>>();
            Assert.Empty(modules.Where(m => m.GetType() == typeof(DependencyTrackingTelemetryModule)));

            var appInsightsOptions = host.Services.GetService<IOptions<ApplicationInsightsLoggerOptions>>();
            Assert.False(appInsightsOptions.Value.HttpAutoCollectionOptions.EnableHttpTriggerExtendedInfoCollection);
        }
    }
}
