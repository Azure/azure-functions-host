// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class ScriptWebHostOptionsSetup : IConfigureOptions<ScriptWebHostOptions>
    {
        private readonly IConfiguration _configuration;

        public ScriptWebHostOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(ScriptWebHostOptions options)
        {
            _configuration.GetSection(ConfigurationSectionNames.WebHost)
                ?.Bind(options);
        }
    }
}
