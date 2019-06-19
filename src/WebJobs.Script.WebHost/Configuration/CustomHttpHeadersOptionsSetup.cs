// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal class CustomHttpHeadersOptionsSetup : IConfigureOptions<CustomHttpHeadersOptions>
    {
        private readonly IConfiguration _configuration;

        public CustomHttpHeadersOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(CustomHttpHeadersOptions options)
        {
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
            var httpGlobalSection = jobHostSection.GetSection(ConfigurationSectionNames.CustomHttpHeaders);
            httpGlobalSection.Bind(options);
        }
    }
}
