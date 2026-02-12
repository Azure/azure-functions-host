// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.AppCapabilities
{
    /// <summary>
    /// Configures the WebHost-level <see cref="AppCapabilitiesOptions"/> by combining:
    ///   1. Capabilities from the active ScriptHost's resolved options (IConfiguration + extensions)
    ///   2. Capabilities reported by the gRPC worker process
    /// Worker values win on collision.
    /// </summary>
    internal sealed class WebHostAppCapabilitiesOptionsSetup : IConfigureOptions<AppCapabilitiesOptions>
    {
        private readonly IAppCapabilitiesStore _workerProvidedAppCapabilitiesStore;

        public WebHostAppCapabilitiesOptionsSetup(
            IScriptHostManager scriptHostManager,
            IAppCapabilitiesStore workerProvidedAppCapabilitiesStore,
            ILogger<WebHostAppCapabilitiesOptionsSetup> logger)
        {
            _workerProvidedAppCapabilitiesStore = workerProvidedAppCapabilitiesStore;
        }

        public void Configure(AppCapabilitiesOptions options)
        {
            foreach (var kvp in _workerProvidedAppCapabilitiesStore.Capabilities)
            {
                options.Capabilities[kvp.Key] = kvp.Value;
            }
        }
    }
}