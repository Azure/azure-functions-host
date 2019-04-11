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
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class FunctionsSyncManager : IFunctionsSyncManager, IDisposable
    {
        private const string HubName = "HubName";
        private const string TaskHubName = "taskHubName";
        private const string Connection = "connection";
        private const string DurableTaskStorageConnectionName = "azureStorageConnectionStringName";
        private const string DurableTask = "durableTask";

        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly ILogger _logger;
        private readonly IEnumerable<WorkerConfig> _workerConfigs;
        private readonly HttpClient _httpClient;
        private readonly ISecretManagerProvider _secretManagerProvider;
        private readonly IConfiguration _configuration;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private readonly IEnvironment _environment;
        private readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);

        private CloudBlockBlob _hashBlob;

        public FunctionsSyncManager(IConfiguration configuration, IHostIdProvider hostIdProvider, IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IOptions<LanguageWorkerOptions> languageWorkerOptions, ILoggerFactory loggerFactory, HttpClient httpClient, ISecretManagerProvider secretManagerProvider, IScriptWebHostEnvironment webHostEnvironment, IEnvironment environment)
        {
            _applicationHostOptions = applicationHostOptions;
            _logger = loggerFactory?.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _httpClient = httpClient;
            _secretManagerProvider = secretManagerProvider;
            _configuration = configuration;
            _hostIdProvider = hostIdProvider;
            _webHostEnvironment = webHostEnvironment;
            _environment = environment;
        }

        public async Task<SyncTriggersResult> TrySyncTriggersAsync(bool checkHash = false)
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
                if (checkHash && hashBlob == null)
                {
                    // short circuit before doing any work in cases where
                    // we're asked to check/update hash but don't have
                    // storage access
                    return result;
                }

                var payload = await GetSyncTriggersPayload();
                string json = JsonConvert.SerializeObject(payload);

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
            if (environment.IsCoreToolsEnvironment())
            {
                // don't sync triggers when running locally or not running in app service in general
                return false;
            }

            if (environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey) == null)
            {
                // We don't have the encryption key required for SetTriggers,
                // so sync calls would fail auth anyways.
                // This might happen in other not core tools environments for example.
                return false;
            }

            if (webHostEnvironment.InStandbyMode)
            {
                // don’t sync triggers when in standby mode
                return false;
            }

            // Windows (Dedicated/Consumption)
            // Linux Consumption
            if ((environment.IsAppServiceWindowsEnvironment() || environment.IsLinuxContainerEnvironment()) &&
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

        public async Task<JArray> GetSyncTriggersPayload()
        {
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            var functionsMetadata = WebFunctionsManager.GetFunctionsMetadata(hostOptions, _workerConfigs, _logger, includeProxies: true);

            // Add trigger information used by the ScaleController
            JObject result = new JObject();
            var triggers = await GetFunctionTriggers(functionsMetadata, hostOptions);
            return new JArray(triggers);
        }

        internal async Task<IEnumerable<JObject>> GetFunctionTriggers(IEnumerable<FunctionMetadata> functionsMetadata, ScriptJobHostOptions hostOptions)
        {
            var durableTaskConfig = await ReadDurableTaskConfig();
            var triggers = (await functionsMetadata
                .Where(f => !f.IsProxy)
                .Select(f => f.ToFunctionTrigger(hostOptions))
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

            if (FileUtility.FileExists(Path.Combine(hostOptions.RootScriptPath, ScriptConstants.ProxyMetadataFileName)))
            {
                // This is because we still need to scale function apps that are proxies only
                triggers = triggers.Append(JObject.FromObject(new { type = "routingTrigger" }));
            }

            return triggers;
        }

        private async Task<Dictionary<string, string>> ReadDurableTaskConfig()
        {
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            string hostJsonPath = Path.Combine(hostOptions.RootScriptPath, ScriptConstants.HostMetadataFileName);
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
            // On private stamps with no ssl certificate use http instead.
            if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.SkipSslValidation) == "1")
            {
                protocol = "http";
            }

            var hostname = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName);
            // Linux Dedicated on AppService doesn't have WEBSITE_HOSTNAME
            hostname = string.IsNullOrWhiteSpace(hostname)
                ? $"{Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName)}.azurewebsites.net"
                : hostname;

            var url = $"{protocol}://{hostname}/operations/settriggers";

            return new HttpRequestMessage(HttpMethod.Post, url);
        }

        // This function will call POST https://{app}.azurewebsites.net/operation/settriggers with the content
        // of triggers. It'll verify app ownership using a SWT token valid for 5 minutes. It should be plenty.
        private async Task<(bool, string)> SetTriggersAsync(string content)
        {
            var token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(5));

            _logger.LogDebug($"SyncTriggers content: {content}");

            using (var request = BuildSetTriggersRequest())
            {
                // This has to start with Mozilla because the frontEnd checks for it.
                request.Headers.Add("User-Agent", "Mozilla/5.0");
                request.Headers.Add("x-ms-site-restricted-token", token);
                request.Content = new StringContent(content, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug($"SyncTriggers call succeeded.");
                    return (true, null);
                }
                else
                {
                    string message = $"SyncTriggers call failed. StatusCode={response.StatusCode}";
                    _logger.LogDebug(message);
                    return (false, message);
                }
            }
        }

        public void Dispose()
        {
            _syncSemaphore.Dispose();
        }
    }
}
