// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class ScriptApplicationHostOptionsSetup : IConfigureOptions<ScriptApplicationHostOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly IScriptWebHostEnvironment _hostEnvironment;

        public ScriptApplicationHostOptionsSetup(IConfiguration configuration, IScriptWebHostEnvironment hostEnvironment)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
        }

        public void Configure(ScriptApplicationHostOptions options)
        {
            _configuration.GetSection(ConfigurationSectionNames.WebHost)
                    ?.Bind(options);

            if (_hostEnvironment.InStandbyMode)
            {
                // If we're in standby mode, override relevant properties with values
                // to be used by the placeholder site.
                // Important that we use paths that are different than the configured paths
                // to ensure that placeholder files are isolated
                string tempRoot = Path.GetTempPath();

                options.LogPath = Path.Combine(tempRoot, @"Functions\Standby\Logs");
                options.ScriptPath = Path.Combine(tempRoot, @"Functions\Standby\WWWRoot");
                options.SecretsPath = Path.Combine(tempRoot, @"Functions\Standby\Secrets");
                options.IsSelfHost = options.IsSelfHost;
            }
        }
    }
}
