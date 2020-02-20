﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class FunctionsSyncManager : IFunctionsSyncManager, IDisposable
    {
        private const string HubName = "HubName";
        private const string TaskHubName = "taskHubName";
        private const string Connection = "connection";
        private const string DurableTaskV1StorageConnectionName = "azureStorageConnectionStringName";
        private const string DurableTaskV2StorageOptions = "storageOptions";
        private const string DurableTaskV2StorageConnectionName = "connectionStringName";
        private const string DurableTask = "durableTask";

        // 45 alphanumeric characters gives us a buffer in our table/queue/blob container names.
        private const int MaxTaskHubNameSize = 45;
        private const int MinTaskHubNameSize = 3;
        private const string TaskHubPadding = "Hub";

        private readonly Regex versionRegex = new Regex(@"Version=(?<majorversion>\d)\.\d\.\d");

        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly ISecretManagerProvider _secretManagerProvider;
        private readonly IConfiguration _configuration;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private readonly IEnvironment _environment;
        private readonly HostNameProvider _hostNameProvider;
        private readonly IFunctionMetadataProvider _functionMetadataProvider;
        private readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);

        private CloudBlockBlob _hashBlob;

        public FunctionsSyncManager(IConfiguration configuration, IHostIdProvider hostIdProvider, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, ILogger<FunctionsSyncManager> logger, HttpClient httpClient, ISecretManagerProvider secretManagerProvider, IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment, HostNameProvider hostNameProvider, IFunctionMetadataProvider functionMetadataProvider)
        {
            _applicationHostOptions = applicationHostOptions;
            _logger = logger;
            _httpClient = httpClient;
            _secretManagerProvider = secretManagerProvider;
            _configuration = configuration;
            _hostIdProvider = hostIdProvider;
            _webHostEnvironment = webHostEnvironment;
            _environment = environment;
            _hostNameProvider = hostNameProvider;
            _functionMetadataProvider = functionMetadataProvider;
        }

        internal bool ArmCacheEnabled
        {
            get
            {
                return _environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.AzureWebsiteArmCacheEnabled, "1") == "1";
            }
        }

        public async Task<SyncTriggersResult> TrySyncTriggersAsync(bool isBackgroundSync = false)
        {
            var result = new SyncTriggersResult
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
                await _syncSemaphore.WaitAsync();

                var hashBlob = await GetHashBlobAsync();
                if (isBackgroundSync && hashBlob == null)
                {
                    // short circuit before doing any work in background sync
                    // cases where we need to check/update hash but don't have
                    // storage access
                    return result;
                }

                var payload = await GetSyncTriggersPayload();
                if (isBackgroundSync && payload.Count == 0)
                {
                    // We don't do background sync for empty triggers.
                    // We've seen error cases where a site temporarily gets into a situation
                    // where it's site content is empty. Doing the empty sync can cause the app
                    // to go idle when it shouldn't.
                    _logger.LogDebug("No functions found. Skipping Sync operation.");
                    return result;
                }

                bool shouldSyncTriggers = true;
                string newHash = null;
                if (isBackgroundSync)
                {
                    newHash = await CheckHashAsync(hashBlob, payload.Content);
                    shouldSyncTriggers = newHash != null;
                }

                if (shouldSyncTriggers)
                {
                    var (success, error) = await SetTriggersAsync(payload.Content);
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
                _logger.LogError(ex, result.Error);
            }
            finally
            {
                _syncSemaphore.Release();
            }

            return result;
        }

        internal static bool IsSyncTriggersEnvironment(IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment)
        {
            if (environment.IsCoreTools())
            {
                // don't sync triggers when running locally or not running in a cloud
                // hosted environment
                return false;
            }

            if (environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey) == null)
            {
                // We don't have the encryption key required for SetTriggers,
                // so sync calls would fail auth anyways.
                // This might happen in when running locally for example.
                return false;
            }

            if (webHostEnvironment.InStandbyMode)
            {
                // don’t sync triggers when in standby mode
                return false;
            }

            // Windows (Dedicated/Consumption)
            // Linux Consumption
            if ((environment.IsWindowsAzureManagedHosting() || environment.IsLinuxConsumption()) &&
                !environment.IsContainerReady())
            {
                // container ready flag not set yet – site not fully specialized/initialized
                return false;
            }

            return true;
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
                    _logger.LogDebug($"SyncTriggers hash (Last='{lastHash}', Current='{currentHash}')");
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
                _logger.LogError(ex, "Error checking SyncTriggers hash");
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
                _logger.LogDebug($"SyncTriggers hash updated to '{hash}'");
            }
            catch (Exception ex)
            {
                // best effort
                _logger.LogError(ex, "Error updating SyncTriggers hash");
            }
        }

        internal async Task<CloudBlockBlob> GetHashBlobAsync()
        {
            if (_hashBlob == null)
            {
                string storageConnectionString = _configuration.GetWebJobsConnectionString(ConnectionStringNames.Storage);
                CloudStorageAccount account = null;
                if (!string.IsNullOrEmpty(storageConnectionString) &&
                    CloudStorageAccount.TryParse(storageConnectionString, out account))
                {
                    string hostId = await _hostIdProvider.GetHostIdAsync(CancellationToken.None);
                    CloudBlobClient blobClient = account.CreateCloudBlobClient();
                    var blobContainer = blobClient.GetContainerReference(ScriptConstants.AzureWebJobsHostsContainerName);
                    string hashBlobPath = $"synctriggers/{hostId}/last";
                    _hashBlob = blobContainer.GetBlockBlobReference(hashBlobPath);
                }
            }
            return _hashBlob;
        }

        public async Task<SyncTriggersPayload> GetSyncTriggersPayload()
        {
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            var functionsMetadata = _functionMetadataProvider.GetFunctionMetadata();

            // trigger information used by the ScaleController
            var triggers = await GetFunctionTriggers(functionsMetadata, hostOptions);
            var triggersArray = new JArray(triggers);
            int count = triggersArray.Count;

            if (!ArmCacheEnabled)
            {
                // extended format is disabled - just return triggers
                return new SyncTriggersPayload
                {
                    Content = JsonConvert.SerializeObject(triggersArray),
                    Count = count
                };
            }

            // Add triggers to the payload
            JObject result = new JObject();
            result.Add("triggers", triggersArray);

            // Add functions details to the payload
            JObject functions = new JObject();
            string routePrefix = await WebFunctionsManager.GetRoutePrefix(hostOptions.RootScriptPath);
            var functionDetails = await WebFunctionsManager.GetFunctionMetadataResponse(functionsMetadata, hostOptions, _hostNameProvider);
            result.Add("functions", new JArray(functionDetails.Select(p => JObject.FromObject(p))));

            // Add functions secrets to the payload
            // Only secret types we own/control can we cache directly
            // Encryption is handled by Antares before storage
            var secretsStorageType = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
            if (string.IsNullOrEmpty(secretsStorageType) ||
                string.Compare(secretsStorageType, "files", StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(secretsStorageType, "blob", StringComparison.OrdinalIgnoreCase) == 0)
            {
                var functionAppSecrets = new FunctionAppSecrets();

                // add host secrets
                var hostSecretsInfo = await _secretManagerProvider.Current.GetHostSecretsAsync();
                functionAppSecrets.Host = new FunctionAppSecrets.HostSecrets
                {
                    Master = hostSecretsInfo.MasterKey,
                    Function = hostSecretsInfo.FunctionKeys,
                    System = hostSecretsInfo.SystemKeys
                };

                // add function secrets
                var httpFunctions = functionsMetadata.Where(p => !p.IsProxy && p.InputBindings.Any(q => q.IsTrigger && string.Compare(q.Type, "httptrigger", StringComparison.OrdinalIgnoreCase) == 0)).Select(p => p.Name).ToArray();
                functionAppSecrets.Function = new FunctionAppSecrets.FunctionSecrets[httpFunctions.Length];
                for (int i = 0; i < httpFunctions.Length; i++)
                {
                    var currFunctionName = httpFunctions[i];
                    var currSecrets = await _secretManagerProvider.Current.GetFunctionSecretsAsync(currFunctionName);
                    functionAppSecrets.Function[i] = new FunctionAppSecrets.FunctionSecrets
                    {
                        Name = currFunctionName,
                        Secrets = currSecrets
                    };
                }

                result.Add("secrets", JObject.FromObject(functionAppSecrets));
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
                _logger.LogWarning($"SyncTriggers payload of length '{json.Length}' exceeds max length of '{ScriptConstants.MaxTriggersStringLength}'. Reverting to minimal format.");
                return new SyncTriggersPayload
                {
                    Content = JsonConvert.SerializeObject(triggersArray),
                    Count = count
                };
            }

            return new SyncTriggersPayload
            {
                Content = json,
                Count = count
            };
        }

        internal async Task<IEnumerable<JObject>> GetFunctionTriggers(IEnumerable<FunctionMetadata> functionsMetadata, ScriptJobHostOptions hostOptions)
        {
            var triggers = (await functionsMetadata
                .Where(f => !f.IsProxy)
                .Select(f => f.ToFunctionTrigger(hostOptions))
                .WhenAll())
                .Where(t => t != null);

            if (triggers.Any(IsDurableTrigger))
            {
                DurableConfig durableTaskConfig = await ReadDurableTaskConfig();
                // If any host level durable config values, we need to apply them to all durable triggers
                if (durableTaskConfig.HasValues())
                {
                    triggers = triggers.Select(t => UpdateDurableFunctionConfig(t, durableTaskConfig));
                }
            }

            if (FileUtility.FileExists(Path.Combine(hostOptions.RootScriptPath, ScriptConstants.ProxyMetadataFileName)))
            {
                // This is because we still need to scale function apps that are proxies only
                triggers = triggers.Append(JObject.FromObject(new { type = "routingTrigger" }));
            }

            return triggers;
        }

        private static bool IsDurableTrigger(JObject trigger)
        {
            return trigger["type"]?.ToString().Equals("orchestrationTrigger", StringComparison.OrdinalIgnoreCase) == true
                || trigger["type"]?.ToString().Equals("entityTrigger", StringComparison.OrdinalIgnoreCase) == true
                || trigger["type"]?.ToString().Equals("activityTrigger", StringComparison.OrdinalIgnoreCase) == true;
        }

        private static JObject UpdateDurableFunctionConfig(JObject trigger, DurableConfig durableTaskConfig)
        {
            if (IsDurableTrigger(trigger))
            {
                if (durableTaskConfig.HubName != null)
                {
                    trigger[TaskHubName] = durableTaskConfig.HubName;
                }

                if (durableTaskConfig.Connection != null)
                {
                    trigger[Connection] = durableTaskConfig.Connection;
                }
            }
            return trigger;
        }

        private async Task<DurableConfig> ReadDurableTaskConfig()
        {
            JObject hostJson = null;
            JObject durableHostConfig = null;
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            string hostJsonPath = Path.Combine(hostOptions.RootScriptPath, ScriptConstants.HostMetadataFileName);
            if (FileUtility.FileExists(hostJsonPath))
            {
                hostJson = JObject.Parse(await FileUtility.ReadAsync(hostJsonPath));

                // get the DurableTask extension config section
                if (hostJson != null &&
                    hostJson.TryGetValue("extensions", StringComparison.OrdinalIgnoreCase, out JToken extensionsValue))
                {
                    // we will allow case insensitivity given it is likely user hand edited
                    // see https://github.com/Azure/azure-functions-durable-extension/issues/111
                    var extensions = extensionsValue as JObject;
                    if (extensions != null &&
                        extensions.TryGetValue(DurableTask, StringComparison.OrdinalIgnoreCase, out JToken durableTaskValue))
                    {
                        durableHostConfig = durableTaskValue as JObject;
                    }
                }
            }

            var durableMajorVersion = await GetDurableMajorVersionAsync(hostJson, hostOptions);
            if (durableMajorVersion == null || durableMajorVersion.Equals("1"))
            {
                return GetDurableV1Config(durableHostConfig);
            }
            else
            {
                // v2 or greater
                return GetDurableV2Config(durableHostConfig);
            }
        }

        // This is a stopgap approach to get the Durable extension version. It duplicates some logic in ExtensionManager.cs.
        private async Task<string> GetDurableMajorVersionAsync(JObject hostJson, ScriptJobHostOptions hostOptions)
        {
            bool isUsingBundles = hostJson != null && hostJson.TryGetValue("extensionBundle", StringComparison.OrdinalIgnoreCase, out _);
            if (isUsingBundles)
            {
                // TODO: As of 2019-12-12, there are no extension bundles for version 2.x of Durable.
                // This may change in the future.
                return "1";
            }

            string binPath = binPath = Path.Combine(hostOptions.RootScriptPath, "bin");
            string metadataFilePath = Path.Combine(binPath, ScriptConstants.ExtensionsMetadataFileName);
            if (!FileUtility.FileExists(metadataFilePath))
            {
                return null;
            }

            var extensionMetadata = JObject.Parse(await FileUtility.ReadAsync(metadataFilePath));
            var extensionItems = extensionMetadata["extensions"]?.ToObject<List<ExtensionReference>>();

            var durableExtension = extensionItems?.FirstOrDefault(ext => string.Equals(ext.Name, "DurableTask", StringComparison.OrdinalIgnoreCase));
            if (durableExtension == null)
            {
                return null;
            }

            var versionMatch = versionRegex.Match(durableExtension.TypeName);
            if (!versionMatch.Success)
            {
                return null;
            }

            // Grab the captured group.
            return versionMatch.Groups["majorversion"].Captures[0].Value;
        }

        private DurableConfig GetDurableV1Config(JObject durableHostConfig)
        {
            var config = new DurableConfig();
            if (durableHostConfig != null)
            {
                if (durableHostConfig.TryGetValue(HubName, StringComparison.OrdinalIgnoreCase, out JToken nameValue) && nameValue != null)
                {
                    config.HubName = nameValue.ToString();
                }

                if (durableHostConfig.TryGetValue(DurableTaskV1StorageConnectionName, StringComparison.OrdinalIgnoreCase, out nameValue) && nameValue != null)
                {
                    config.Connection = nameValue.ToString();
                }
            }

            return config;
        }

        private DurableConfig GetDurableV2Config(JObject durableHostConfig)
        {
            var config = new DurableConfig();

            if (durableHostConfig != null)
            {
                if (durableHostConfig.TryGetValue(HubName, StringComparison.OrdinalIgnoreCase, out JToken nameValue) && nameValue != null)
                {
                    config.HubName = nameValue.ToString();
                }

                if (durableHostConfig.TryGetValue(DurableTaskV2StorageOptions, StringComparison.OrdinalIgnoreCase, out JToken storageOptions) && (storageOptions as JObject) != null)
                {
                    if (((JObject)storageOptions).TryGetValue(DurableTaskV2StorageConnectionName, StringComparison.OrdinalIgnoreCase, out nameValue) && nameValue != null)
                    {
                        config.Connection = nameValue.ToString();
                    }
                }
            }

            if (config.HubName == null)
            {
                config.HubName = GetDefaultDurableV2HubName();
            }

            return config;
        }

        // This logic will eventually be moved to ScaleController once it has access to version information.
        private string GetDefaultDurableV2HubName()
        {
            // See https://github.com/Azure/azure-functions-durable-extension/blob/eb186eadb73a21d0efdc33cd7603fde5d802cab9/src/WebJobs.Extensions.DurableTask/Options/DurableTaskOptions.cs#L42
            string hubName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);
            // See https://github.com/Azure/azure-functions-durable-extension/blob/eb186eadb73a21d0efdc33cd7603fde5d802cab9/src/WebJobs.Extensions.DurableTask/Options/AzureStorageOptions.cs#L145
            hubName = new string(hubName.ToCharArray()
                .Where(char.IsLetterOrDigit)
                .Take(MaxTaskHubNameSize)
                .ToArray());
            if (hubName.Length < MinTaskHubNameSize)
            {
                hubName += TaskHubPadding;
            }

            return hubName;
        }

        internal HttpRequestMessage BuildSetTriggersRequest()
        {
            var protocol = "https";
            if (_environment.GetEnvironmentVariable(EnvironmentSettingNames.SkipSslValidation) == "1")
            {
                // On private stamps with no ssl certificate use http instead.
                protocol = "http";
            }

            var hostname = _hostNameProvider.Value;
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
                request.Headers.Add(ScriptConstants.SiteTokenHeaderName, token);
                request.Content = new StringContent(content, Encoding.UTF8, "application/json");

                _logger.LogDebug($"Making SyncTriggers request (RequestId={requestId}, Uri={request.RequestUri.ToString()}, Content={sanitizedContentString}).");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug($"SyncTriggers call succeeded.");
                    return (true, null);
                }
                else
                {
                    string message = $"SyncTriggers call failed (StatusCode={response.StatusCode}).";
                    _logger.LogDebug(message);
                    return (false, message);
                }
            }
        }

        public void Dispose()
        {
            _syncSemaphore.Dispose();
        }

        public class SyncTriggersPayload
        {
            public string Content { get; set; }

            public int Count { get; set; }
        }

        private class DurableConfig
        {
            public string HubName { get; set; }

            public string Connection { get; set; }

            public bool HasValues()
            {
                return this.HubName != null || this.Connection != null;
            }
        }
    }
}