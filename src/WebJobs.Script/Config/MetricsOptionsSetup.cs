// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal class MetricsOptionsSetup : IConfigureOptions<MetricsOptions>
    {
        private readonly IEnvironment _environment;

        public MetricsOptionsSetup(IEnvironment environment)
        {
            _environment = environment;
        }

        public void Configure(MetricsOptions options)
        {
            options.AppName = _environment.GetAzureWebsiteUniqueSlotName() ?? string.Empty;
            options.SubscriptionId = _environment.GetSubscriptionId() ?? string.Empty;
        }
    }
}