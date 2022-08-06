// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Az.ServerlessSecurity.Platform;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    internal class ServerlessSecurityDefenderOptionsSetup : IConfigureOptions<ServerlessSecurityDefenderOptions>
    {
        private readonly IEnvironment _environment;

        public ServerlessSecurityDefenderOptionsSetup(IEnvironment environment)
        {
            _environment = environment;
        }

        public void Configure(ServerlessSecurityDefenderOptions options)
        {
            options.EnableDefender = _environment.GetEnvironmentVariable("AZURE_FUNCTIONS_SECURITY_AGENT_ENABLED") == "1";
        }
    }
}