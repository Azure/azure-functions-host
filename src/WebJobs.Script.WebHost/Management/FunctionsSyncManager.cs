// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public sealed class FunctionsSyncManager : IFunctionsSyncManager, IDisposable
    {
        private const string HubName = "HubName";
        private const string TaskHubName = "taskHubName";
        private const string Connection = "connection";
        private const string DurableTaskStorageConnectionName = "azureStorageConnectionStringName";
        private const string DurableTask = "durableTask";

        private readonly ScriptHostConfiguration _hostConfig;
        private readonly ILogger _logger;
        private readonly ISecretManager _secretManager;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly ScriptSettingsManager _settings;
        private readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);
        private readonly TraceWriter _traceWriter;

        private CloudBlockBlob _hashBlob;

        public FunctionsSyncManager(ScriptHostConfiguration hostConfig, ILoggerFactory loggerFactory, ISecretManager secretManager, ScriptSettingsManager settings, HttpClient httpClient = null)
        {
            _hostConfig = hostConfig;
            _logger = loggerFactory.CreateLogger<FunctionsSyncManager>();
            _traceWriter = hostConfig.TraceWriter.WithDefaults(typeof(FunctionsSyncManager).FullName);
            _secretManager = secretManager;
            _settings = settings;

            _httpClient = httpClient;
            if (_httpClient == null)
            {
                _httpClient = new HttpClient();
                _ownsHttpClient = true;
            }
        }

        internal bool ArmCacheEnabled
        {
            get
            {
                return _settings.GetSettingOrDefault(EnvironmentSettingNames.AzureWebsiteArmCacheEnabled, "1") == "1";
            }
        }

        public async Task<SyncTriggersResult> TrySyncTriggersAsync(bool checkHash = false)
        {
            var result = new SyncTriggersResult
            {
                Success = true
            };

            if (!IsSyncTriggersEnvironment(_settings))
            {
                result.Success = false;
                result.Error = "Invalid environment for SyncTriggers operation.";
                _logger.LogWarning(result.Error);
                _traceWriter.Warning(result.Error);
                return result;
            }

            try
            {
                await _syncSemaphore.WaitAsync();

                var hashBlob = GetHashBlob();
                if (checkHash && hashBlob == null)
                {
                    // short circuit before doing any work in cases where
                    // we're asked to check/update hash but don't have
                    // storage access
                    return result;
                }

                string json = await GetSyncTriggersPayload();

                bool shouldSyncTriggers = true;
                string newHash = null;
                if (checkHash)
                {
                    newHash = await CheckHashAsync(hashBlob, json);
                    shouldSyncTriggers = newHash != null;
                }

                if (shouldSyncTriggers)
                {
                    var (success, error) = await SetTriggersAsync(json);
                    if (success && newHash != null)
                    {
                        await UpdateHashAsync(hashBlob, newHash);
                    }
                    result.Success = success;
                    result.Error = error;
                }
            }
            catch (Exception ex)
            {
                // best effort - log error and continue
                result.Success = false;
                result.Error = "SyncTriggers operation failed.";
                _logger.LogError(0, ex, result.Error);
                _traceWriter.Error(result.Error, ex);
            }
            finally
            {
                _syncSemaphore.Release();
            }

            return result;
        }

        internal static bool IsSyncTriggersEnvironment(ScriptSettingsManager settings)
        {
            if (settings.IsCoreToolsEnvironment)
            {
                // don't sync triggers when running locally or not running in a cloud
                // hosted environment
                return false;
            }

            if (settings.GetSetting(EnvironmentSettingNames.WebsiteAuthEncryptionKey) == null)
            {
                // We don't have the encryption key required for SetTriggers,
                // so sync calls would fail auth anyways.
                // This might happen in when running locally for example.
                return false;
            }

            // only want to do background sync triggers when NOT
            // in standby mode
            // ContainerReady/ConfigurationReady will be false locally - it's based on a DWAS environment flag
            return !WebScriptHostManager.InStandbyMode && settings.ContainerReady && settings.ConfigurationReady;
        }

        internal async Task<string> CheckHashAsync(CloudBlockBlob hashBlob, string content)
        {
            try
            {
                // compute the current hash value and compare it with
                // the last stored value
                string currentHash = null;
                using (var sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
                    currentHash = hash
                        .Aggregate(new StringBuilder(), (a, b) => a.Append(b.ToString("x2")))
                        .ToString();
                }

                // get the last hash value if present
                string lastHash = null;
                if (await hashBlob.ExistsAsync())
                {
                    lastHash = await hashBlob.DownloadTextAsync();
                    string message = $"SyncTriggers hash (Last='{lastHash}', Current='{currentHash}')";
                    _logger.LogDebug(message);
                    _traceWriter.Verbose(message);
                }

                if (string.Compare(currentHash, lastHash) != 0)
                {
                    // hash will need to be updated - return the
                    // new hash value
                    return currentHash;
                }
            }
            catch (Exception ex)
            {
                // best effort
                string error = "Error checking SyncTriggers hash";
                _logger.LogError(0, ex, error);
                _traceWriter.Error(error, ex);
            }

            // if the last and current hash values are the same,
            // or if any error occurs, return null
            return null;
        }

        internal async Task UpdateHashAsync(CloudBlockBlob hashBlob, string hash)
        {
            try
            {
                // hash value has changed or was not yet stored
                // update the last hash value in storage
                await hashBlob.UploadTextAsync(hash);
                string message = $"SyncTriggers hash updated to '{hash}'";
                _logger.LogDebug(message);
                _traceWriter.Verbose(message);
            }
            catch (Exception ex)
            {
                // best effort
                string error = "Error updating SyncTriggers hash";
                _logger.LogError(0, ex, error);
                _traceWriter.Error(error, ex);
            }
        }

        internal CloudBlockBlob GetHashBlob()
        {
            if (_hashBlob == null)
            {
                string hostId = _hostConfig.HostConfig.HostId;
                string storageConnectionString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
                CloudStorageAccount account = null;
                if (!string.IsNullOrEmpty(storageConnectionString) &&
                    CloudStorageAccount.TryParse(storageConnectionString, out account) && !string.IsNullOrEmpty(hostId))
                {
                    CloudBlobClient blobClient = account.CreateCloudBlobClient();
                    var blobContainer = blobClient.GetContainerReference(ScriptConstants.AzureWebJobsHostsContainerName);
                    
                    string hashBlobPath = $"synctriggers/{hostId}/last";
                    _hashBlob = blobContainer.GetBlockBlobReference(hashBlobPath);
                }
            }
            return _hashBlob;
        }

        public async Task<string> GetSyncTriggersPayload()
        {
            var functionsMetadata = WebFunctionsManager.GetFunctionsMetadata(_hostConfig, _logger);

            // trigger information used by the ScaleController
            var triggers = await GetFunctionTriggers(functionsMetadata);
            var triggersArray = new JArray(triggers);

            if (!ArmCacheEnabled)
            {
                // extended format is disabled - just return triggers
                return JsonConvert.SerializeObject(triggersArray);
            }

            // Add triggers to the payload
            JObject result = new JObject();
            result.Add("triggers", triggersArray);

            // Add functions details to the payload
            JObject functions = new JObject();
            string routePrefix = WebFunctionsManager.GetRoutePrefix(_hostConfig.RootScriptPath);
            var functionDetails = await WebFunctionsManager.GetFunctionMetadataResponse(functionsMetadata, _hostConfig);
            result.Add("functions", new JArray(functionDetails.Select(p => JObject.FromObject(p))));

            // Add functions secrets to the payload
            // Only secret types we own/control can we cache directly
            // Encryption is handled by Antares before storage
            var secretsStorageType = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
            if (string.IsNullOrEmpty(secretsStorageType) ||
                string.Compare(secretsStorageType, "files", StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(secretsStorageType, "blob", StringComparison.OrdinalIgnoreCase) == 0)
            {
                JObject secrets = new JObject();
                result.Add("secrets", secrets);

                // add host secrets
                var hostSecretsInfo = await _secretManager.GetHostSecretsAsync();
                var hostSecrets = new JObject();
                hostSecrets.Add("master", hostSecretsInfo.MasterKey);
                hostSecrets.Add("function", JObject.FromObject(hostSecretsInfo.FunctionKeys));
                hostSecrets.Add("system", JObject.FromObject(hostSecretsInfo.SystemKeys));
                secrets.Add("host", hostSecrets);

                // add function secrets
                var functionSecrets = new JArray();
                var httpFunctions = functionsMetadata.Where(p => !p.IsProxy && p.InputBindings.Any(q => q.IsTrigger && string.Compare(q.Type, "httptrigger", StringComparison.OrdinalIgnoreCase) == 0)).Select(p => p.Name);
                foreach (var functionName in httpFunctions)
                {
                    var currSecrets = await _secretManager.GetFunctionSecretsAsync(functionName);
                    var currElement = new JObject()
                    {
                        { "name", functionName },
                        { "secrets", JObject.FromObject(currSecrets) }
                    };
                    functionSecrets.Add(currElement);
                }
                secrets.Add("function", functionSecrets);
            }
            else
            {
                // TODO: handle other external key storage types
                // like KeyVault when the feature comes online
            }

            string json = JsonConvert.SerializeObject(result);
            if (json.Length > ScriptConstants.MaxTriggersStringLength)
            {
                // The settriggers call to the FE enforces a max request size
                // limit. If we're over limit, revert to the minimal triggers
                // format.
                string warning = $"SyncTriggers payload of length '{json.Length}' exceeds max length of '{ScriptConstants.MaxTriggersStringLength}'. Reverting to minimal format.";
                _logger.LogWarning(warning);
                _traceWriter.Warning(warning);
                return JsonConvert.SerializeObject(triggersArray);
            }

            return json;
        }

        private async Task<IEnumerable<JObject>> GetFunctionTriggers(IEnumerable<FunctionMetadata> functionsMetadata)
        {
            var durableTaskConfig = await ReadDurableTaskConfig();
            var triggers = (await functionsMetadata
                .Where(f => !f.IsProxy)
                .Select(f => f.ToFunctionTrigger(_hostConfig))
                .WhenAll())
                .Where(t => t != null)
                .Select(t =>
                {
                    // if we have a durableTask hub name and the function trigger is either orchestrationTrigger OR activityTrigger,
                    // add a property "taskHubName" with durable task hub name.
                    if (durableTaskConfig.Any()
                        && (t["type"]?.ToString().Equals("orchestrationTrigger", StringComparison.OrdinalIgnoreCase) == true
                            || t["type"]?.ToString().Equals("activityTrigger", StringComparison.OrdinalIgnoreCase) == true))
                    {
                        if (durableTaskConfig.ContainsKey(HubName))
                        {
                            t[TaskHubName] = durableTaskConfig[HubName];
                        }

                        if (durableTaskConfig.ContainsKey(Connection))
                        {
                            t[Connection] = durableTaskConfig[Connection];
                        }
                    }
                    return t;
                });

            if (FileUtility.FileExists(Path.Combine(_hostConfig.RootScriptPath, ScriptConstants.ProxyMetadataFileName)))
            {
                // This is because we still need to scale function apps that are proxies only
                triggers = triggers.Append(JObject.FromObject(new { type = "routingTrigger" }));
            }

            return triggers;
        }

        private async Task<Dictionary<string, string>> ReadDurableTaskConfig()
        {
            string hostJsonPath = Path.Combine(_hostConfig.RootScriptPath, ScriptConstants.HostMetadataFileName);
            var config = new Dictionary<string, string>();
            if (FileUtility.FileExists(hostJsonPath))
            {
                var hostJson = JObject.Parse(await FileUtility.ReadAsync(hostJsonPath));
                JToken durableTaskValue;

                // we will allow case insensitivity given it is likely user hand edited
                // see https://github.com/Azure/azure-functions-durable-extension/issues/111
                //
                // We're looking for {VALUE}
                // {
                //     "durableTask": {
                //         "hubName": "{VALUE}",
                //         "azureStorageConnectionStringName": "{VALUE}"
                //     }
                // }
                if (hostJson.TryGetValue(DurableTask, StringComparison.OrdinalIgnoreCase, out durableTaskValue) && durableTaskValue != null)
                {
                    try
                    {
                        var kvp = (JObject)durableTaskValue;
                        if (kvp.TryGetValue(HubName, StringComparison.OrdinalIgnoreCase, out JToken nameValue) && nameValue != null)
                        {
                            config.Add(HubName, nameValue.ToString());
                        }

                        if (kvp.TryGetValue(DurableTaskStorageConnectionName, StringComparison.OrdinalIgnoreCase, out nameValue) && nameValue != null)
                        {
                            config.Add(Connection, nameValue.ToString());
                        }
                    }
                    catch (Exception)
                    {
                        throw new InvalidDataException("Invalid host.json configuration for 'durableTask'.");
                    }
                }
            }

            return config;
        }

        internal static HttpRequestMessage BuildSetTriggersRequest()
        {
            var protocol = "https";
            if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.SkipSslValidation) == "1")
            {
                // On private stamps with no ssl certificate use http instead.
                protocol = "http";
            }

            var hostname = HostNameProvider.Value;
            var url = $"{protocol}://{hostname}/operations/settriggers";

            return new HttpRequestMessage(HttpMethod.Post, url);
        }

        // This function will call POST https://{app}.azurewebsites.net/operation/settriggers with the content
        // of triggers. It'll verify app ownership using a SWT token valid for 5 minutes. It should be plenty.
        private async Task<(bool, string)> SetTriggersAsync(string content)
        {
            var token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(5));

            string sanitizedContentString = content;
            if (ArmCacheEnabled)
            {
                // sanitize the content before logging
                var sanitizedContent = JToken.Parse(content);
                if (sanitizedContent.Type == JTokenType.Object)
                {
                    ((JObject)sanitizedContent).Remove("secrets");
                    sanitizedContentString = sanitizedContent.ToString();
                }
            }

            using (var request = BuildSetTriggersRequest())
            {
                var requestId = Guid.NewGuid().ToString();
                request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, requestId);
                request.Headers.Add("User-Agent", ScriptConstants.FunctionsUserAgent);
                request.Headers.Add("x-ms-site-restricted-token", token);
                request.Content = new StringContent(content, Encoding.UTF8, "application/json");

                string message = $"Making SyncTriggers request (RequestId={requestId}, Uri={request.RequestUri.ToString()}, Content={sanitizedContentString}).";
                _logger.LogDebug(message);
                _traceWriter.Verbose(message);

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    message = $"SyncTriggers call succeeded.";
                    _logger.LogDebug(message);
                    _traceWriter.Verbose(message);
                    return (true, null);
                }
                else
                {
                    message = $"SyncTriggers call failed (StatusCode={response.StatusCode}).";
                    _logger.LogDebug(message);
                    _traceWriter.Verbose(message);
                    return (false, message);
                }
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _syncSemaphore.Dispose();

            if (_httpClient != null && _ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}