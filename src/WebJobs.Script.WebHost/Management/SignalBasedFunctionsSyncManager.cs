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
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private readonly IEnvironment _environment;

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
            _webHostEnvironment = webHostEnvironment;
            _environment = environment;
        }

        public override async Task<TriggersOperationResult> TrySyncTriggersAsync(bool isBackgroundSync = false)
        {
            var result = new TriggersOperationResult
            {
                Success = true
            };

            if (!IsSyncTriggersEnvironment(_webHostEnvironment, _environment))
            {
                result.Success = false;
                result.Error = "Invalid environment for SyncTriggers operation.";
                _logger.LogWarning(result.Error);

                return result;
            }

            try
            {
                var payload = await GetSyncTriggersPayload();
                string contentHash = ComputeContentHash(payload.Content);
                await _meshServiceClient.NotifyTriggersChanged(contentHash);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = "Failed to notify triggers changed.";
                _logger.LogError(ex, result.Error);
            }

            return result;
        }

        private static string ComputeContentHash(string content)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));

            return Convert.ToHexStringLower(hash);
        }
    }
}
