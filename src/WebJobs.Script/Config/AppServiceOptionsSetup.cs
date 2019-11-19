// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal class AppServiceOptionsSetup : IConfigureOptions<AppServiceOptions>
    {
        private readonly IEnvironment _environment;

        public AppServiceOptionsSetup(IEnvironment environment)
        {
            _environment = environment;
        }

        public void Configure(AppServiceOptions options)
        {
            options.AppName = _environment.GetAzureWebsiteUniqueSlotName() ?? string.Empty;
            options.SubscriptionId = _environment.GetSubscriptionId() ?? string.Empty;
            options.RuntimeSiteName = _environment.GetRuntimeSiteName() ?? string.Empty;
            options.SlotName = _environment.GetSlotName() ?? string.Empty;
        }
    }
}