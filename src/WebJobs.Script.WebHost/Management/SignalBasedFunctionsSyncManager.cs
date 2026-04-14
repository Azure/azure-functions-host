// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class SignalBasedFunctionsSyncManager : FunctionsSyncManager
    {
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly ILogger _logger;

        public SignalBasedFunctionsSyncManager(
            IHostIdProvider hostIdProvider,
            IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions,
            ILogger<SignalBasedFunctionsSyncManager> logger,
            IHttpClientFactory httpClientFactory,
            ISecretManagerProvider secretManagerProvider,
            IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment,
            HostNameProvider hostNameProvider,
            IFunctionMetadataManager functionMetadataManager,
            IAzureBlobStorageProvider azureBlobStorageProvider,
            IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions,
            IScriptHostManager scriptHostManager,
            IMeshServiceClient meshServiceClient)
            : base(hostIdProvider, applicationHostOptions, logger, httpClientFactory, secretManagerProvider, webHostEnvironment, environment, hostNameProvider, functionMetadataManager, azureBlobStorageProvider, functionsHostingConfigOptions, scriptHostManager)
        {
            _meshServiceClient = meshServiceClient;
            _logger = logger;
        }

        // Overrides the default settriggers HTTP call to instead notify the mesh service
        // with a content hash of the triggers payload
        protected override async Task<(bool Success, string ErrorMessage)> SetTriggersAsync(string content)
        {
            try
            {
                string contentHash = ComputeContentHash(content);
                await _meshServiceClient.NotifyTriggersChanged(contentHash);

                return (true, null);
            }
            catch (Exception ex)
            {
                string message = "Failed to notify triggers changed.";
                _logger.LogError(ex, message);

                return (false, message);
            }
        }

        private static string ComputeContentHash(string content)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));

            return Convert.ToHexStringLower(hash);
        }
    }
}
