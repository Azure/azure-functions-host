// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    /// <summary>
    /// Setup that sets <see cref="JobHostOptions"/> properties based
    /// on script host configuration conventions.
    /// </summary>
    public class JobHostOptionsSetup : IConfigureOptions<JobHostOptions>
    {
        private readonly IConfiguration _configuration;

        public JobHostOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public void Configure(JobHostOptions options)
        {
            // TODO: Why isn't this code doing anything?
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
        }
    }
}
