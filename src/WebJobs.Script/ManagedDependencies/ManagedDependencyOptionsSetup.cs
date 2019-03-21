// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.ManagedDependencies
{
    internal class ManagedDependencyOptionsSetup : IConfigureOptions<ManagedDependencyOptions>
    {
        private readonly IConfiguration _configuration;

        public ManagedDependencyOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(ManagedDependencyOptions options)
        {
            IConfigurationSection jobHostSection = _configuration.GetSection(ConfigurationSectionNames.JobHost);
            var managedDependencySection = jobHostSection.GetSection(ConfigurationSectionNames.ManagedDependency);
            managedDependencySection.Bind(options);
        }
    }
}
