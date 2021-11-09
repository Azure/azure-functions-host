// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class RetryOptionsSetup : IConfigureOptions<RetryOptions>
    {
        private readonly IConfiguration _configuration;

        public RetryOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(RetryOptions options)
        {
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
            var retrySection = jobHostSection.GetSection(ConfigurationSectionNames.Retry);
            retrySection.Bind(options);
        }
    }
}
