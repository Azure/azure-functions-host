// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class AzureMonitorOptionsSetup : IConfigureOptions<AzureMonitorOptions>
    {
        private readonly IEnvironment _env;

        public AzureMonitorOptionsSetup(IEnvironment env)
        {
            _env = env;
        }

        public void Configure(AzureMonitorOptions options)
        {
            options.IsAzureMonitorTimeIsoFormatEnabled = IsAzureMonitorTimeIsoFormatEnabled();
        }

        private bool IsAzureMonitorTimeIsoFormatEnabled()
        {
            string enabledString = _env.GetEnvironmentVariable(EnvironmentSettingNames.AzureMonitorTimeIsoFormatEnabled);
            if (bool.TryParse(enabledString, out bool result))
            {
                return result;
            }
            if (int.TryParse(enabledString, out int enabledInt))
            {
                return Convert.ToBoolean(enabledInt);
            }
            return false;
        }
    }
}
