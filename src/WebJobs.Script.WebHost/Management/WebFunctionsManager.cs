// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class WebFunctionsManager : IWebFunctionsManager
    {
        private const string HubName = "HubName";
        private const string TaskHubName = "taskHubName";
        private const string Connection = "connection";
        private const string DurableTaskStorageConnectionName = "azureStorageConnectionStringName";
        private const string DurableTask = "durableTask";

        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly ILogger _logger;
        private readonly HttpClient _client;
        private readonly IEnumerable<WorkerConfig> _workerConfigs;

        public WebFunctionsManager(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IOptions<LanguageWorkerOptions> languageWorkerOptions, ILoggerFactory loggerFactory, HttpClient client)
        {
            _applicationHostOptions = applicationHostOptions;
            _logger = loggerFactory?.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
            _client = client;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
        }

        /// <summary>
        /// Calls into ScriptHost to retrieve list of FunctionMetadata
        /// and maps them to FunctionMetadataResponse.
        /// </summary>
        /// <param name="request">Current HttpRequest for figuring out baseUrl</param>
        /// <returns>collection of FunctionMetadataResponse</returns>
        public async Task<IEnumerable<FunctionMetadataResponse>> GetFunctionsMetadata(HttpRequest request, bool includeProxies)
        {
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();

            string routePrefix = await GetRoutePrefix(hostOptions.RootScriptPath);
            var tasks = GetFunctionsMetadata(includeProxies).Select(p => p.ToFunctionMetadataResponse(request, hostOptions, routePrefix));
            return await tasks.WhenAll();
        }

        /// <summary>
        /// It handles creating a new function or updating an existing one.
        /// It attempts to clean left over artifacts from a possible previous function with the same name
        /// if config is changed, then `configChanged` is set to true so the caller can call SyncTriggers if needed.
        /// </summary>
        /// <param name="name">name of the function to be created</param>
        /// <param name="functionMetadata">in case of update for function.json</param>
        /// <param name="request">Current HttpRequest.</param>
        /// <returns>(success, configChanged, functionMetadataResult)</returns>
        public async Task<(bool, bool, FunctionMetadataResponse)> CreateOrUpdate(string name, FunctionMetadataResponse functionMetadata, HttpRequest request)
        {
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            var configChanged = false;
            var functionDir = Path.Combine(hostOptions.RootScriptPath, name);

            // Make sure the function folder exists
            if (!FileUtility.DirectoryExists(functionDir))
            {
                // Cleanup any leftover artifacts from a function with the same name before.
                DeleteFunctionArtifacts(functionMetadata);
                Directory.CreateDirectory(functionDir);
            }

            string newConfig = null;
            string configPath = Path.Combine(functionDir, ScriptConstants.FunctionMetadataFileName);
            string dataFilePath = FunctionMetadataExtensions.GetTestDataFilePath(name, hostOptions);

            // If files are included, write them out
            if (functionMetadata?.Files != null)
            {
                // If the config is passed in the file collection, save it and don't process it as a file
                if (functionMetadata.Files.TryGetValue(ScriptConstants.FunctionMetadataFileName, out newConfig))
                {
                    functionMetadata.Files.Remove(ScriptConstants.FunctionMetadataFileName);
                }

                // Delete all existing files in the directory. This will also delete current function.json, but it gets recreated below
                FileUtility.DeleteDirectoryContentsSafe(functionDir);

                await functionMetadata
                    .Files
                    .Select(e => FileUtility.WriteAsync(Path.Combine(functionDir, e.Key), e.Value))
                    .WhenAll();
            }

            // Get the config (if it was not already passed in as a file)
            if (newConfig == null && functionMetadata?.Config != null)
            {
                newConfig = JsonConvert.SerializeObject(functionMetadata?.Config, Formatting.Indented);
            }

            // Get the current config, if any
            string currentConfig = null;
            if (FileUtility.FileExists(configPath))
            {
                currentConfig = await FileUtility.ReadAsync(configPath);
            }

            // Save the file and set changed flag is it has changed. This helps optimize the syncTriggers call
            if (newConfig != currentConfig)
            {
                await FileUtility.WriteAsync(configPath, newConfig);
                configChanged = true;
            }

            if (functionMetadata.TestData != null)
            {
                await FileUtility.WriteAsync(dataFilePath, functionMetadata.TestData);
            }

            (var success, var functionMetadataResult) = await TryGetFunction(name, request); // test_data took from incoming request, it will not exceed the limit
            return (success, configChanged, functionMetadataResult);
        }

        /// <summary>
        /// maps a functionName to its FunctionMetadataResponse
        /// </summary>
        /// <param name="name">Function name to retrieve</param>
        /// <param name="request">Current HttpRequest</param>
        /// <returns>(success, FunctionMetadataResponse)</returns>
        public async Task<(bool, FunctionMetadataResponse)> TryGetFunction(string name, HttpRequest request)
        {
            // TODO: DI (FACAVAL) Follow up with ahmels - Since loading of function metadata is no longer tied to the script host, we
            // should be able to inject an IFunctionMetadataManager here and bypass this step.
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            var functionMetadata = FunctionMetadataManager.ReadFunctionMetadata(Path.Combine(hostOptions.RootScriptPath, name), null, _workerConfigs, new Dictionary<string, ICollection<string>>(), fileSystem: FileUtility.Instance);
            if (functionMetadata != null)
            {
                string routePrefix = await GetRoutePrefix(hostOptions.RootScriptPath);
                return (true, await functionMetadata.ToFunctionMetadataResponse(request, hostOptions, routePrefix));
            }
            else
            {
                return (false, null);
            }
        }

        /// <summary>
        /// Delete a function and all it's artifacts.
        /// </summary>
        /// <param name="function">Function to be deleted</param>
        /// <returns>(success, errorMessage)</returns>
        public (bool, string) TryDeleteFunction(FunctionMetadataResponse function)
        {
            try
            {
                var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
                var functionPath = function.GetFunctionPath(hostOptions);
                if (!string.IsNullOrEmpty(functionPath))
                {
                    FileUtility.DeleteDirectoryContentsSafe(functionPath);
                }

                DeleteFunctionArtifacts(function);
                return (true, string.Empty);
            }
            catch (Exception e)
            {
                return (false, e.ToString());
            }
        }

        /// <summary>
        /// Try to perform sync triggers to the scale controller
        /// </summary>
        /// <returns>(success, error)</returns>
        public async Task<(bool success, string error)> TrySyncTriggers()
        {
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            var durableTaskConfig = await ReadDurableTaskConfig();
            var functionsTriggers = (await GetFunctionsMetadata()
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
                functionsTriggers = functionsTriggers.Append(JObject.FromObject(new { type = "routingTrigger" }));
            }

            return await InternalSyncTriggers(functionsTriggers);
        }

        internal static HttpRequestMessage BuildSyncTriggersRequest()
        {
            var protocol = "https";
            // On private stamps with no ssl certificate use http instead.
            if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.SkipSslValidation) == "1")
            {
                protocol = "http";
            }

            var url = $"{protocol}://{Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)}/operations/settriggers";

            return new HttpRequestMessage(HttpMethod.Post, url);
        }

        // This function will call POST https://{app}.azurewebsites.net/operation/settriggers with the content
        // of triggers. It'll verify app owner ship using a SWT token valid for 5 minutes. It should be plenty.
        private async Task<(bool, string)> InternalSyncTriggers(IEnumerable<JObject> triggers)
        {
            var content = JsonConvert.SerializeObject(triggers);
            var token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(5));

            using (var request = BuildSyncTriggersRequest())
            {
                // This has to start with Mozilla because the frontEnd checks for it.
                request.Headers.Add("User-Agent", "Mozilla/5.0");
                request.Headers.Add("x-ms-site-restricted-token", token);
                request.Content = new StringContent(content, Encoding.UTF8, "application/json");

                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode
                    ? (true, string.Empty)
                    : (false, $"Sync triggers failed with: {response.StatusCode}");
            }
        }

        internal IEnumerable<FunctionMetadata> GetFunctionsMetadata(bool includeProxies = false)
        {
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            var functionDirectories = FileUtility.EnumerateDirectories(hostOptions.RootScriptPath);
            IEnumerable<FunctionMetadata> functionsMetadata = FunctionMetadataManager.ReadFunctionsMetadata(functionDirectories, null, _workerConfigs, _logger, fileSystem: FileUtility.Instance);

            if (includeProxies)
            {
                // get proxies metadata
                var values = ProxyMetadataManager.ReadProxyMetadata(hostOptions.RootScriptPath, _logger);
                var proxyFunctionsMetadata = values.Item1;
                if (proxyFunctionsMetadata?.Count > 0)
                {
                    functionsMetadata = proxyFunctionsMetadata.Concat(functionsMetadata);
                }
            }

            return functionsMetadata;
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

        private void DeleteFunctionArtifacts(FunctionMetadataResponse function)
        {
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            var testDataPath = function.GetFunctionTestDataFilePath(hostOptions);

            if (!string.IsNullOrEmpty(testDataPath))
            {
                FileUtility.DeleteFileSafe(testDataPath);
            }
        }

        // TODO : Due to lifetime scoping issues (this service lifetime is longer than the lifetime
        // of HttpOptions sourced from host.json) we're reading the http route prefix anew each time
        // to ensure we have the latest configured value.
        internal static async Task<string> GetRoutePrefix(string rootScriptPath)
        {
            string routePrefix = "api";

            string hostConfigFile = Path.Combine(rootScriptPath, ScriptConstants.HostMetadataFileName);
            if (File.Exists(hostConfigFile))
            {
                string hostConfigJson = await File.ReadAllTextAsync(hostConfigFile);
                try
                {
                    var jo = JObject.Parse(hostConfigJson);
                    var token = jo.SelectToken("extensions['http'].routePrefix", errorWhenNoMatch: false);
                    if (token != null)
                    {
                        routePrefix = (string)token;
                    }
                }
                catch
                {
                    // best effort
                }
            }

            return routePrefix;
        }
    }
}
