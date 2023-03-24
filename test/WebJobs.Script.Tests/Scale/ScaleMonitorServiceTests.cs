// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Scale
{
    public class ScaleMonitorServiceTests
    {
        [Theory]
        [InlineData(true, true)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public async Task ScaleMonitorService_RegistersExpected(bool runtimeScaleMonitoringEnabled, bool targetBaseScalingEnabled)
        {
            if (runtimeScaleMonitoringEnabled)
            {
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsRuntimeScaleMonitoringEnabled, "1");
            }
            if (!targetBaseScalingEnabled)
            {
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.TargetBaseScalingEnabled, "0");
            }

            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            IHostBuilder hostBuilder = new HostBuilder()
            .ConfigureLogging(b =>
            {
                b.AddProvider(testLoggerProvider);
            })
            .ConfigureDefaultTestWebScriptHost(null, true);

            using (var host = hostBuilder.Build())
            {
                await host.StartAsync();

                var scaleOptions = host.Services.GetService<IOptions<ScaleOptions>>();
                Assert.Equal(scaleOptions.Value.IsTargetScalingEnabled, targetBaseScalingEnabled);
                if (runtimeScaleMonitoringEnabled)
                {
                    Assert.Contains(testLoggerProvider.GetAllLogMessages(), x => x.FormattedMessage.StartsWith("Runtime scale monitoring is enabled."));
                    if (scaleOptions.Value.IsTargetScalingEnabled)
                    {
                        Assert.Contains(testLoggerProvider.GetAllLogMessages(), x => x.FormattedMessage.Contains("\"IsTargetScalingEnabled\": true"));
                    }
                    else
                    {
                        Assert.Contains(testLoggerProvider.GetAllLogMessages(), x => x.FormattedMessage.Contains("\"IsTargetScalingEnabled\": false"));
                    }
                }
                else
                {
                    Assert.DoesNotContain(testLoggerProvider.GetAllLogMessages(), x => x.FormattedMessage.StartsWith("Scale monitor service is started."));
                }

                // Clean
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsRuntimeScaleMonitoringEnabled, string.Empty);
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.TargetBaseScalingEnabled, string.Empty);
            }
        }
    }
}
