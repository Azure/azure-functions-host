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
        private readonly IEnvironment _environment;

        public ScriptApplicationHostOptionsSetup(IConfiguration configuration)
            : this(configuration, SystemEnvironment.Instance)
        {
        }

        internal ScriptApplicationHostOptionsSetup(IConfiguration configuration, IEnvironment environment)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public void Configure(ScriptApplicationHostOptions options)
        {
            _configuration.GetSection(ConfigurationSectionNames.WebHost)
                    ?.Bind(options);

            // Indicate that a WebHost is hosting the ScriptHost
            options.HasParentScope = true;

            if (_environment.IsPlaceholderModeEnabled())
            {
                // If we're in standby mode, override relevant properties with values
                // to be used by the placeholder site.
                // Important that we use paths that are different than the configured paths
                // to ensure that placeholder files are isolated
                string tempRoot = Path.GetTempPath();

                options.LogPath = Path.Combine(tempRoot, @"functions\standby\logs");
                options.ScriptPath = Path.Combine(tempRoot, @"functions\standby\wwwroot");
                options.SecretsPath = Path.Combine(tempRoot, @"functions\standby\secrets");
                options.IsSelfHost = options.IsSelfHost;
            }
        }
    }
}
