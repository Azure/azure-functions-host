// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class HostCorsOptionsSetup : IConfigureOptions<HostCorsOptions>
    {
        private readonly IConfiguration _configuration;

        public HostCorsOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(HostCorsOptions options)
        {
            var allowedOriginsString = _configuration.GetValue<string>(EnvironmentSettingNames.CorsAllowedOrigins);

            IEnumerable<string> corsAllowedOrigins = Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(allowedOriginsString))
            {
                corsAllowedOrigins = JsonConvert.DeserializeObject<IEnumerable<string>>(allowedOriginsString);
            }
            options.AllowedOrigins = corsAllowedOrigins;

            var supportCredentialsString = _configuration.GetValue<bool?>(EnvironmentSettingNames.CorsSupportCredentials);
            options.SupportCredentials = supportCredentialsString ?? false;
        }
    }
}
