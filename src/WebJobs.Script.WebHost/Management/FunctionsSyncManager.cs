// Copyright (c) .NET Foundation. All rights reserved.
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
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
        private const string DurableTaskV2StorageOptions = "storageProvider";
        private const string DurableTaskV2StorageConnectionName = "connectionStringName";
        private const string DurableTaskV2MaxConcurrentActivityFunctions = "maxConcurrentActivityFunctions";
        private const string DurableTaskV2MaxConcurrentOrchestratorFunctions = "maxConcurrentOrchestratorFunctions";
        private const string DurableTask = "durableTask";

        // 45 alphanumeric characters gives us a buffer in our table/queue/blob container names.
        private const int MaxTaskHubNameSize = 45;
        private const int MinTaskHubNameSize = 3;
        private const string TaskHubPadding = "Hub";

        //Managed Kubernetes build service variables
        private const string ManagedKubernetesBuildServicePort = "8181";
        private const string ManagedKubernetesBuildServiceName = "k8se-build-service";
        private const string ManagedKubernetesBuildServiceNamespace = "k8se-system";

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
        private readonly IFunctionMetadataManager _functionMetadataManager;
        private readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);
        private readonly IAzureBlobStorageProvider _azureBlobStorageProvider;

        private BlobClient _hashBlobClient;

        public FunctionsSyncManager(IConfiguration configuration, IHostIdProvider hostIdProvider, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, ILogger<FunctionsSyncManager> logger, IHttpClientFactory httpClientFactory, ISecretManagerProvider secretManagerProvider, IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment, HostNameProvider hostNameProvider, IFunctionMetadataManager functionMetadataManager, IAzureBlobStorageProvider azureBlobStorageProvider)
        {
            _applicationHostOptions = applicationHostOptions;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _secretManagerProvider = secretManagerProvider;
            _configuration = configuration;
            _hostIdProvider = hostIdProvider;
            _webHostEnvironment = webHostEnvironment;
            _environment = environment;
            _hostNameProvider = hostNameProvider;
            _functionMetadataManager = functionMetadataManager;
            _azureBlobStorageProvider = azureBlobStorageProvider;
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

                PrepareSyncTriggers();

                var hashBlobClient = await GetHashBlobAsync();
                if (isBackgroundSync && hashBlobClient == null && !_environment.IsKubernetesManagedHosting())
                {
                    // short circuit before doing any work in background sync
                    // cases where we need to check/update hash but don't have
                    // storage access in non-Kubernetes environments.
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
                if (isBackgroundSync && !_environment.IsKubernetesManagedHosting())
                {
                    newHash = await CheckHashAsync(hashBlobClient, payload.Content);
                    shouldSyncTriggers = newHash != null;
                }

                if (shouldSyncTriggers)
                {
                    var (success, error) = await SetTriggersAsync(payload.Content);
                    if (success && newHash != null)
                    {
                        await UpdateHashAsync(hashBlobClient, newHash);
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

        /// <summary>
        /// SyncTriggers is performed whenever deployments or other changes are made to the application.
        /// There are some operations we want to perform whenever this happens.
        /// </summary>
        private void PrepareSyncTriggers()
        {
            // We clear cache to ensure that secrets are reloaded. This is important because secrets are part
            // of the StartupContext payload (see StartupContextProvider) and that payload comes from the
            // SyncTriggers operation. So there's a chicken and egg situation here. Consider the following scenario:
            //   - app is using blob storage for keys
            //   - a SyncTriggers operation has happened previously and the StartupContext has key info
            //   - app instances initialize keys from StartupContext (keys aren't loaded from storage)
            //   - user updates the app to use a new storage account
            //   - a SyncTriggers operation is performed
            //   - the app initializes from StartupContext, and **previous old key info is loaded**
            //   - the SyncTriggers operation uses this old key info, so trigger cache is never updated with new key info
            //   - Portal/ARM APIs will continue to show old key info.
            // By clearing cache, we ensure that this host instance reloads keys when they're requested, and the SyncTriggers
            // operation will contain current keys.
            if (_secretManagerProvider.SecretsEnabled)
            {
                _secretManagerProvider.Current.ClearCache();
            }
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
            if ((environment.IsWindowsAzureManagedHosting() || environment.IsAnyLinuxConsumption()) &&
                !environment.IsContainerReady())
            {
                // container ready flag not set yet – site not fully specialized/initialized
                return false;
            }

            return true;
        }

        internal async Task<string> CheckHashAsync(BlobClient hashBlobClient, string content)
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
                if (await hashBlobClient.ExistsAsync())
                {
                    var downloadResponse = await hashBlobClient.DownloadAsync();
                    using (StreamReader reader = new StreamReader(downloadResponse.Value.Content))
                    {
                        lastHash = reader.ReadToEnd();
                    }
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

        internal async Task UpdateHashAsync(BlobClient hashBlobClient, string hash)
        {
            try
            {
                // hash value has changed or was not yet stored
                // update the last hash value in storage
                using (Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(hash)))
                {
                    await hashBlobClient.UploadAsync(stream, overwrite: true);
                }
                _logger.LogDebug($"SyncTriggers hash updated to '{hash}'");
            }
            catch (Exception ex)
            {
                // best effort
                _logger.LogError(ex, "Error updating SyncTriggers hash");
            }
        }

        internal async Task<BlobClient> GetHashBlobAsync()
        {
            if (_hashBlobClient == null)
            {
                if (_azureBlobStorageProvider.TryCreateBlobServiceClientFromConnection(ConnectionStringNames.Storage, out BlobServiceClient blobClient))
                {
                    string hostId = await _hostIdProvider.GetHostIdAsync(CancellationToken.None);
                    var blobContainerClient = blobClient.GetBlobContainerClient(ScriptConstants.AzureWebJobsHostsContainerName);
                    string hashBlobPath = $"synctriggers/{hostId}/last";
                    _hashBlobClient = blobContainerClient.GetBlobClient(hashBlobPath);
                }
            }
            return _hashBlobClient;
        }

        public async Task<SyncTriggersPayload> GetSyncTriggersPayload()
        {
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            var functionsMetadata = _functionMetadataManager.GetFunctionMetadata().Where(m => !m.IsProxy());

            // trigger information used by the ScaleController
            var triggers = await GetFunctionTriggers(functionsMetadata, hostOptions);
            var triggersArray = new JArray(triggers);
            int count = triggersArray.Count;

            // Form the base minimal result
            string hostId = await _hostIdProvider.GetHostIdAsync(CancellationToken.None);
            JObject result = GetMinimalPayload(hostId, triggersArray);

            if (!ArmCacheEnabled)
            {
                // extended format is disabled - just return minimal results
                return new SyncTriggersPayload
                {
                    Content = JsonConvert.SerializeObject(result),
                    Count = count
                };
            }

            // Add all listable functions details to the payload
            JObject functions = new JObject();
            var listableFunctions = _functionMetadataManager.GetFunctionMetadata().Where(m => !m.IsCodeless());
            var functionDetails = await WebFunctionsManager.GetFunctionMetadataResponse(listableFunctions, hostOptions, _hostNameProvider);
            result.Add("functions", new JArray(functionDetails.Select(p => JObject.FromObject(p))));

            // TEMP: refactor this code to properly add extensions in all scenario(#7394)
            // Add the host.json extensions to the payload
            if (_environment.IsKubernetesManagedHosting())
            {
                JObject extensionsPayload = await GetHostJsonExtensionsAsync(_applicationHostOptions, _logger);
                if (extensionsPayload != null)
                {
                    result.Add("extensions", extensionsPayload);
                }
            }

            if (_secretManagerProvider.SecretsEnabled)
            {
                // Add functions secrets to the payload
                // Only secret types we own/control can we cache directly
                // Encryption is handled by Antares before storage
                var secretsStorageType = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
                if (string.IsNullOrEmpty(secretsStorageType) ||
                    string.Equals(secretsStorageType, "files", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(secretsStorageType, "blob", StringComparison.OrdinalIgnoreCase))
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
                    var httpFunctions = functionsMetadata.Where(p => !p.IsProxy() && p.InputBindings.Any(q => q.IsTrigger && string.Equals(q.Type, "httptrigger", StringComparison.OrdinalIgnoreCase))).Select(p => p.Name).ToArray();
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
            }

            string json = JsonConvert.SerializeObject(result);

            if (json.Length > ScriptConstants.MaxTriggersStringLength && !_environment.IsKubernetesManagedHosting())
            {
                // The settriggers call to the FE enforces a max request size limit.
                // If we're over limit, revert to the minimal triggers format.
                _logger.LogWarning($"SyncTriggers payload of length '{json.Length}' exceeds max length of '{ScriptConstants.MaxTriggersStringLength}'. Reverting to minimal format.");

                var minimalResult = GetMinimalPayload(hostId, triggersArray);
                json = JsonConvert.SerializeObject(minimalResult);
            }

            return new SyncTriggersPayload
            {
                Content = json,
                Count = count
            };
        }

        private JObject GetMinimalPayload(string hostId, JArray triggersArray)
        {
            JObject result = new JObject
            {
                { "triggers", triggersArray }
            };

            if (_environment.IsFlexConsumptionSku())
            {
                // Currently we're only sending the HostId for Flex Consumption. Eventually we'll do this for all SKUs.
                // When the HostId is sent, ScaleController will use it directly rather than compute it itself.
                result["hostId"] = hostId;
            }

            return result;
        }

        internal static async Task<JObject> GetHostJsonExtensionsAsync(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, ILogger logger)
        {
            var hostOptions = applicationHostOptions.CurrentValue.ToHostOptions();
            string hostJsonPath = Path.Combine(hostOptions.RootScriptPath, ScriptConstants.HostMetadataFileName);
            if (FileUtility.FileExists(hostJsonPath))
            {
                try
                {
                    var hostJson = JObject.Parse(await FileUtility.ReadAsync(hostJsonPath));
                    if (hostJson.TryGetValue("extensions", out JToken token))
                    {
                        return (JObject)token;
                    }
                    else
                    {
                        return null;
                    }
                }
                catch (JsonException ex)
                {
                    logger.LogWarning($"Unable to parse host configuration file '{hostJsonPath}'. : {ex}");
                    return null;
                }
            }

            return null;
        }

        internal async Task<IEnumerable<JObject>> GetFunctionTriggers(IEnumerable<FunctionMetadata> functionsMetadata, ScriptJobHostOptions hostOptions)
        {
            var triggers = (await functionsMetadata
                .Where(f => !f.IsProxy())
                .Select(f => f.ToFunctionTrigger(hostOptions))
                .WhenAll())
                .Where(t => t != null);

            // TODO: We should remove extension-specific logic from the Host. See: https://github.com/Azure/azure-functions-host/issues/5390
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

                if (durableTaskConfig.StorageProvider != null)
                {
                    trigger[DurableTaskV2StorageOptions] = durableTaskConfig.StorageProvider;
                }

                if (durableTaskConfig.MaxConcurrentOrchestratorFunctions != 0)
                {
                    trigger[DurableTaskV2MaxConcurrentOrchestratorFunctions] = durableTaskConfig.MaxConcurrentOrchestratorFunctions;
                }

                if (durableTaskConfig.MaxConcurrentActivityFunctions != 0)
                {
                    trigger[DurableTaskV2MaxConcurrentActivityFunctions] = durableTaskConfig.MaxConcurrentActivityFunctions;
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
            string metadataFilePath;
            bool isUsingBundles = hostJson != null && hostJson.TryGetValue("extensionBundle", StringComparison.OrdinalIgnoreCase, out _);
            // This feature flag controls whether to opt out of the SyncTrigger metadata fix for OOProc DF apps from https://github.com/Azure/azure-functions-host/pull/9331
            if (FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagEnableLegacyDurableVersionCheck))
            {
                // using legacy behavior, which concludes that out of process DF apps (including .NET isolated) are using DF Extension V1.x
                // as a result, the SyncTriggers payload for these apps will be missing some metadata like "taskHubName"
                if (isUsingBundles)
                {
                    return "1";
                }

                string binPath = binPath = Path.Combine(hostOptions.RootScriptPath, "bin");
                metadataFilePath = Path.Combine(binPath, ScriptConstants.ExtensionsMetadataFileName);
                if (!FileUtility.FileExists(metadataFilePath))
                {
                    return null;
                }
            }
            else
            {
                if (isUsingBundles)
                {
                    // From Functions runtime V4 onwards, only bundles >= V2.x is supported, which implies the app should be using DF V2 or greater.
                    return "2";
                }

                // If the app is not using bundles, we look for extensions.json
                if (!Utility.TryResolveExtensionsMetadataPath(hostOptions.RootScriptPath, out string metadataDirectoryPath, out _))
                {
                    return null;
                }
                metadataFilePath = Path.Combine(metadataDirectoryPath, ScriptConstants.ExtensionsMetadataFileName);
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

                    config.StorageProvider = storageOptions;
                }

                if (durableHostConfig.TryGetValue(DurableTaskV2MaxConcurrentOrchestratorFunctions, StringComparison.OrdinalIgnoreCase, out JToken maxConcurrentOrchestratorFunctions) && maxConcurrentOrchestratorFunctions != null)
                {
                    config.MaxConcurrentOrchestratorFunctions = int.Parse(maxConcurrentOrchestratorFunctions.ToString());
                }

                if (durableHostConfig.TryGetValue(DurableTaskV2MaxConcurrentActivityFunctions, StringComparison.OrdinalIgnoreCase, out JToken maxConcurrentActivityFunctions) && maxConcurrentActivityFunctions != null)
                {
                    config.MaxConcurrentActivityFunctions = int.Parse(maxConcurrentActivityFunctions.ToString());
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
            var url = default(string);
            if (_environment.IsAnyKubernetesEnvironment())
            {
                var hostName = _environment.GetEnvironmentVariable("FUNCTIONS_API_SERVER") ??
                    _environment.GetEnvironmentVariable("BUILD_SERVICE_HOSTNAME");
                if (string.IsNullOrEmpty(hostName))
                {
                    hostName = $"http://{ManagedKubernetesBuildServiceName}.{ManagedKubernetesBuildServiceNamespace}.svc.cluster.local:{ManagedKubernetesBuildServicePort}";
                }
                url = $"{hostName}/api/operations/settriggers";
            }
            else
            {
                var protocol = "https";
                if (_environment.GetEnvironmentVariable(EnvironmentSettingNames.SkipSslValidation) == "1")
                {
                    // On private stamps with no ssl certificate use http instead.
                    protocol = "http";
                }

                var hostname = _hostNameProvider.Value;
                url = $"{protocol}://{hostname}/operations/settriggers";
            }

            return new HttpRequestMessage(HttpMethod.Post, url);
        }

        // This function will call POST https://{app}.azurewebsites.net/operation/settriggers with the content
        // of triggers. It'll verify app ownership using a SWT token valid for 5 minutes. It should be plenty.
        private async Task<(bool Success, string ErrorMessage)> SetTriggersAsync(string content)
        {
            string swtToken = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(5));
            string jwtToken = JwtTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(5));

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
                request.Headers.Add(ScriptConstants.SiteRestrictedTokenHeaderName, swtToken);
                request.Headers.Add(ScriptConstants.SiteTokenHeaderName, jwtToken);
                request.Content = new StringContent(content, Encoding.UTF8, "application/json");

                if (_environment.IsManagedAppEnvironment())
                {
                    request.Headers.Add("K8SE-APP-NAME", _environment.GetEnvironmentVariable("CONTAINER_APP_NAME"));
                    request.Headers.Add("K8SE-APP-NAMESPACE", _environment.GetEnvironmentVariable("CONTAINER_APP_NAMESPACE"));
                    request.Headers.Add("K8SE-APP-REVISION", _environment.GetEnvironmentVariable("CONTAINER_APP_REVISION"));
                }
                else if (_environment.IsKubernetesManagedHosting())
                {
                    request.Headers.Add(ScriptConstants.KubernetesManagedAppName, _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName));
                    request.Headers.Add(ScriptConstants.KubernetesManagedAppNamespace, _environment.GetEnvironmentVariable(EnvironmentSettingNames.PodNamespace));
                }

                _logger.LogDebug($"Making SyncTriggers request (RequestId={requestId}, Uri={request.RequestUri.ToString()}, Content={sanitizedContentString}).");

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("SyncTriggers call succeeded.");
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

            public JToken StorageProvider { get; set; }

            public int MaxConcurrentActivityFunctions { get; set; }

            public int MaxConcurrentOrchestratorFunctions { get; set; }

            public bool HasValues()
            {
                return this.HubName != null || this.Connection != null || this.StorageProvider != null || this.MaxConcurrentOrchestratorFunctions != 0 || this.MaxConcurrentOrchestratorFunctions != 0;
            }
        }
    }
}
