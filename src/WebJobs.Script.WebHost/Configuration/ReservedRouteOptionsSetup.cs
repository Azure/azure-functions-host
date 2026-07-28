// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal sealed class ReservedRouteOptionsSetup(IEnvironment environment) : IConfigureOptions<ReservedRouteOptions>
    {
        public void Configure(ReservedRouteOptions options)
        {
            options.DisableReservedRouteEnforcement =
                FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableReservedRouteEnforcement, environment);
            options.AdminWarmupRouteEnabled = environment.IsAdminWarmupRouteEnabled();
        }
    }
}
