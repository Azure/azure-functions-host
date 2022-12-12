// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class FunctionsHostingConfigOptionsSetup : IConfigureOptions<FunctionsHostingConfigOptions>
    {
        private readonly IConfiguration _configuration;

        public FunctionsHostingConfigOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(FunctionsHostingConfigOptions options)
        {
            IConfigurationSection section = _configuration.GetSection(ScriptConstants.FunctionsHostingConfigSectionName);
            if (section != null)
            {
                foreach (var pair in section.GetChildren())
                {
                    if (!string.IsNullOrEmpty(pair.Value))
                    {
                        options.Features.TryAdd(pair.Key, pair.Value);
                    }
                }
            }
        }
    }
}
