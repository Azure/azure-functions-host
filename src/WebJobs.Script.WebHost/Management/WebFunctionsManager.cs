// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class WebFunctionsManager : IWebFunctionsManager
    {
        private readonly ScriptHostConfiguration _config;
        private readonly ILogger _logger;

        public WebFunctionsManager(ScriptHostConfiguration config, ILoggerFactory loggerFactory)
        {
            _config = config;
            _logger = loggerFactory?.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
        }

        public async Task<IEnumerable<FunctionMetadataResponse>> GetFunctionsMetadata()
        {
            string routePrefix = GetRoutePrefix(_config.RootScriptPath);
            var tasks = GetFunctionsMetadata(_config, _logger).Select(p => p.ToFunctionMetadataResponse(_config, routePrefix));
            return await tasks.WhenAll();
        }

        internal static IEnumerable<FunctionMetadata> GetFunctionsMetadata(ScriptHostConfiguration config, ILogger logger)
        {
            var functionDirectories = FileUtility.EnumerateDirectories(config.RootScriptPath);
            var functionErrors = new Dictionary<string, Collection<string>>();
            IEnumerable<FunctionMetadata> functionsMetadata = ScriptHost.ReadFunctionMetadata(functionDirectories, NullTraceWriter.Instance, logger, functionErrors);

            return functionsMetadata;
        }

        internal static async Task<IEnumerable<FunctionMetadataResponse>> GetFunctionMetadataResponse(IEnumerable<FunctionMetadata> functionsMetadata, ScriptHostConfiguration hostConfig)
        {
            string routePrefix = GetRoutePrefix(hostConfig.RootScriptPath);
            var tasks = functionsMetadata.Select(p => p.ToFunctionMetadataResponse(hostConfig, routePrefix));

            return await tasks.WhenAll();
        }

        public async Task<(bool, FunctionMetadataResponse)> TryGetFunction(string name)
        {
            string directory = Path.Combine(_config.RootScriptPath, name);
            var functionErrors = new Dictionary<string, Collection<string>>();
            var functionMetadata = ScriptHost.ReadFunctionMetadata(directory, NullTraceWriter.Instance, _logger, functionErrors);
            if (functionMetadata != null)
            {
                string routePrefix = GetRoutePrefix(_config.RootScriptPath);
                return (true, await functionMetadata.ToFunctionMetadataResponse(_config, routePrefix));
            }
            else
            {
                return (false, null);
            }
        }

        // TODO : Due to lifetime scoping issues (this service lifetime is longer than the lifetime
        // of HttpOptions sourced from host.json) we're reading the http route prefix anew each time
        // to ensure we have the latest configured value.
        internal static string GetRoutePrefix(string rootScriptPath)
        {
            string routePrefix = "api";

            string hostConfigFile = Path.Combine(rootScriptPath, ScriptConstants.HostMetadataFileName);
            if (File.Exists(hostConfigFile))
            {
                string hostConfigJson = File.ReadAllText(hostConfigFile);
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