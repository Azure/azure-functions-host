// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class WebFunctionsManager : IWebFunctionsManager
    {
        private readonly ScriptHostConfiguration _config;
        private readonly ILogger _logger;

        public WebFunctionsManager(WebHostSettings webSettings, ILoggerFactory loggerFactory)
        {
            _config = WebHostResolver.CreateScriptHostConfiguration(webSettings);
            _logger = loggerFactory?.CreateLogger(ScriptConstants.LogCategoryKeysController);
        }

        /// <summary>
        /// Calls into ScriptHost to retrieve list of FunctionMetadata
        /// and maps them to FunctionMetadataResponse.
        /// </summary>
        /// <param name="request">Current HttpRequest for figuring out baseUrl</param>
        /// <returns>collection of FunctionMetadataResponse</returns>
        public async Task<IEnumerable<FunctionMetadataResponse>> GetFunctionsMetadata(HttpRequest request)
        {
            return await ScriptHost.ReadFunctionsMetadata(FileUtility.EnumerateDirectories(_config.RootScriptPath), _logger, new Dictionary<string, Collection<string>>())
                .Select(fm => fm.ToFunctionMetadataResponse(request, _config))
                .WhenAll();
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
            var configChanged = false;
            var functionDir = Path.Combine(_config.RootScriptPath, name);

            // Make sure the function folder exists
            if (!FileUtility.DirectoryExists(functionDir))
            {
                // Cleanup any leftover artifacts from a function with the same name before.
                DeleteFunctionArtifacts(functionMetadata);
                Directory.CreateDirectory(functionDir);
            }

            string newConfig = null;
            string configPath = Path.Combine(functionDir, ScriptConstants.FunctionMetadataFileName);
            string dataFilePath = FunctionMetadataExtensions.GetTestDataFilePath(name, _config);

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
            var functionMetadata = ScriptHost.ReadFunctionMetadata(Path.Combine(_config.RootScriptPath, name), _logger, new Dictionary<string, Collection<string>>());
            if (functionMetadata != null)
            {
                return (true, await functionMetadata.ToFunctionMetadataResponse(request, _config));
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
                FileUtility.DeleteDirectoryContentsSafe(function.GetFunctionPath(_config));
                DeleteFunctionArtifacts(function);
                return (true, string.Empty);
            }
            catch (Exception e)
            {
                return (false, e.ToString());
            }
        }

        private void DeleteFunctionArtifacts(FunctionMetadataResponse function)
        {
            // TODO: clear secrets
            // TODO: clear logs
            FileUtility.DeleteFileSafe(function.GetFunctionTestDataFilePath(_config));
        }
    }
}
