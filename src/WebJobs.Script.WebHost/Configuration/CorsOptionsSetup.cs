// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class CorsOptionsSetup : IConfigureOptions<CorsOptions>
    {
        private readonly IEnvironment _env;
        private readonly IOptions<HostCorsOptions> _hostCorsOptions;

        public CorsOptionsSetup(IEnvironment env, IOptions<HostCorsOptions> hostCorsOptions)
        {
            _env = env;
            _hostCorsOptions = hostCorsOptions;
        }

        public void Configure(CorsOptions options)
        {
            if (_env.IsAnyLinuxConsumption())
            {
                string[] allowedOrigins = _hostCorsOptions.Value.AllowedOrigins?.ToArray() ?? Array.Empty<string>();
                var policyBuilder = new CorsPolicyBuilder(allowedOrigins);

                if (_hostCorsOptions.Value.SupportCredentials)
                {
                    policyBuilder = policyBuilder.AllowCredentials();
                }

                policyBuilder.AllowAnyHeader();
                policyBuilder.AllowAnyMethod();

                var policy = policyBuilder.Build();
                options.AddDefaultPolicy(policy);
            }
        }
    }
}
