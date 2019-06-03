// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal class HostHstsOptionsSetup : IConfigureOptions<HostHstsOptions>
    {
        private readonly IConfiguration _configuration;

        public HostHstsOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(HostHstsOptions options)
        {
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
            var hstsSection = jobHostSection.GetSection(ConfigurationSectionNames.Hsts);
            hstsSection.Bind(options);
        }
    }
}
