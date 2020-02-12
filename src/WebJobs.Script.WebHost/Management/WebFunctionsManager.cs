// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class WebFunctionsManager : IWebFunctionsManager
    {
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly ILogger _logger;
        private readonly HttpClient _client;
        private readonly IEnumerable<RpcWorkerConfig> _workerConfigs;
        private readonly ISecretManagerProvider _secretManagerProvider;
        private readonly IFunctionsSyncManager _functionsSyncManager;
        private readonly HostNameProvider _hostNameProvider;
        private readonly IFunctionMetadataProvider _functionMetadataProvider;

        public WebFunctionsManager(IOptionsMonitor<ScriptApplicationHostOptions> applicationHostOptions, IOptions<LanguageWorkerOptions> languageWorkerOptions, ILoggerFactory loggerFactory, HttpClient client, ISecretManagerProvider secretManagerProvider, IFunctionsSyncManager functionsSyncManager, HostNameProvider hostNameProvider, IFunctionMetadataProvider functionMetadataProvider)
        {
            _applicationHostOptions = applicationHostOptions;
            _logger = loggerFactory?.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
            _client = client;
            _workerConfigs = languageWorkerOptions.Value.WorkerConfigs;
            _secretManagerProvider = secretManagerProvider;
            _functionsSyncManager = functionsSyncManager;
            _hostNameProvider = hostNameProvider;
            _functionMetadataProvider = functionMetadataProvider;
        }

        public async Task<IEnumerable<FunctionMetadataResponse>> GetFunctionsMetadata(bool includeProxies)
        {
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            var functionsMetadata = GetFunctionsMetadata(hostOptions, _logger, includeProxies);

            return await GetFunctionMetadataResponse(functionsMetadata, hostOptions, _hostNameProvider);
        }

        internal static async Task<IEnumerable<FunctionMetadataResponse>> GetFunctionMetadataResponse(IEnumerable<FunctionMetadata> functionsMetadata, ScriptJobHostOptions hostOptions, HostNameProvider hostNameProvider)
        {
            string baseUrl = GetBaseUrl(hostNameProvider);
            string routePrefix = await GetRoutePrefix(hostOptions.RootScriptPath);
            var tasks = functionsMetadata.Select(p => p.ToFunctionMetadataResponse(hostOptions, routePrefix, baseUrl));

            return await tasks.WhenAll();
        }

        internal IEnumerable<FunctionMetadata> GetFunctionsMetadata(ScriptJobHostOptions hostOptions, ILogger logger, bool includeProxies = false)
        {
            IEnumerable<FunctionMetadata> functionsMetadata = _functionMetadataProvider.GetFunctionMetadata();

            if (includeProxies)
            {
                // get proxies metadata
                var values = ProxyMetadataManager.ReadProxyMetadata(hostOptions.RootScriptPath, logger);
                var proxyFunctionsMetadata = values.Item1;
                if (proxyFunctionsMetadata?.Count > 0)
                {
                    functionsMetadata = proxyFunctionsMetadata.Concat(functionsMetadata);
                }
            }

            return functionsMetadata;
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

            // we need to sync triggers if config changed, or the files changed
            await _functionsSyncManager.TrySyncTriggersAsync();

            (var success, var functionMetadataResult) = await TryGetFunction(name, request);

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
            var hostOptions = _applicationHostOptions.CurrentValue.ToHostOptions();
            var functionMetadata = _functionMetadataProvider.GetFunctionMetadata(true)
                .FirstOrDefault(metadata => Utility.FunctionNamesMatch(metadata.Name, name));

            if (functionMetadata != null)
            {
                string routePrefix = await GetRoutePrefix(hostOptions.RootScriptPath);
                var baseUrl = $"{request.Scheme}://{request.Host}";
                return (true, await functionMetadata.ToFunctionMetadataResponse(hostOptions, routePrefix, baseUrl));
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
        public async Task<(bool, string)> TryDeleteFunction(FunctionMetadataResponse function)
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

                await _functionsSyncManager.TrySyncTriggersAsync();

                return (true, string.Empty);
            }
            catch (Exception e)
            {
                return (false, e.ToString());
            }
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

        internal static string GetBaseUrl(HostNameProvider hostNameProvider)
        {
            string hostName = hostNameProvider.Value ?? "localhost";
            return $"https://{hostName}";
        }

        internal string GetBaseUrl()
        {
            return GetBaseUrl(_hostNameProvider);
        }
    }
}
