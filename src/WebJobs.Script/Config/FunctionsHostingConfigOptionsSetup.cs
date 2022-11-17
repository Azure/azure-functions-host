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
            ConfigurationRoot configRoot = _configuration as ConfigurationRoot;
            if (configRoot != null)
            {
                IConfigurationProvider provider = configRoot.Providers.SingleOrDefault(x => x.GetType() == typeof(FunctionsHostingConfigProvider));
                if (provider != null)
                {
                    var keys = provider.GetChildKeys(new string[] { }, null);

                    foreach (string key in keys)
                    {
                        if (provider.TryGet(key, out string value))
                        {
                            if (!string.IsNullOrEmpty(value))
                            {
                                options.Features[key] = value;
                            }
                        }
                    }
                }
            }
        }
    }
}
