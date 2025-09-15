// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    internal class RouteHandlingOptionsSetup : IConfigureOptions<RouteHandlingOptions>
    {
        private readonly IConfiguration _configuration;

        public RouteHandlingOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(RouteHandlingOptions options)
        {
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
            var routeHandlingSection = jobHostSection.GetSection(ConfigurationSectionNames.CustomHandlerRouteHandling);

            // If the routeHandling section is not defined in host.json under customHandler, do not bind or validate it.
            if (!routeHandlingSection.Exists())
            {
                return;
            }

            routeHandlingSection.Bind(options);

            // Validation is performed later during host initialization to ensure any errors
            // are surfaced from ScriptHost.InitializeAsync rather than during DI/service creation.
        }
    }
}
