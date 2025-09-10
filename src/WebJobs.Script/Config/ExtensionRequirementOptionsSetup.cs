// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class ExtensionRequirementOptionsSetup : IConfigureOptions<ExtensionRequirementOptions>
    {
        private readonly IConfiguration _configuration;

        public ExtensionRequirementOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(ExtensionRequirementOptions options)
        {
            IConfigurationSection requirementsSection = _configuration.GetSection(ScriptConstants.ExtensionRequirementsSection);
            if (requirementsSection.Exists())
            {
                requirementsSection.Bind(options);
            }
        }
    }
}
