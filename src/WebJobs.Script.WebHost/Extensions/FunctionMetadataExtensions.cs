// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class FunctionMetadataExtensions
    {
        /// <summary>
        /// Maps FunctionMetadata to FunctionMetadataResponse.
        /// </summary>
        /// <param name="functionMetadata">FunctionMetadata to be mapped.</param>
        /// <param name="hostConfig">The host options</param>
        /// <returns>Promise of a FunctionMetadataResponse</returns>
        public static async Task<FunctionMetadataResponse> ToFunctionMetadataResponse(this FunctionMetadata functionMetadata, ScriptHostConfiguration hostConfig, string routePrefix)
        {
            string scmBaseUrl = GetScmBaseUrl();
            string appBaseUrl = GetAppBaseUrl();
            var functionPath = Path.Combine(hostConfig.RootScriptPath, functionMetadata.Name);
            var functionMetadataFilePath = Path.Combine(functionPath, ScriptConstants.FunctionMetadataFileName);

            var response = new FunctionMetadataResponse
            {
                Name = functionMetadata.Name,
                ConfigHref = FilePathToVfsUri(functionMetadataFilePath, scmBaseUrl, hostConfig),
                ScriptRootPathHref = FilePathToVfsUri(functionPath, scmBaseUrl, hostConfig, isDirectory: true),
                Href = GetFunctionHref(functionMetadata.Name, scmBaseUrl),
                Config = await GetFunctionConfig(functionMetadataFilePath),

                // Properties below this comment are not present in the kudu version.
                IsDirect = functionMetadata.IsDirect,
                IsDisabled = functionMetadata.IsDisabled,
                IsProxy = functionMetadata.IsProxy,
                Language = functionMetadata.ScriptType.ToString(),
                InvokeUrlTemplate = GetFunctionInvokeUrlTemplate(appBaseUrl, functionMetadata, routePrefix)
            };

            if (!string.IsNullOrEmpty(hostConfig.TestDataPath))
            {
                var testDataFilePath = functionMetadata.GetTestDataFilePath(hostConfig);
                response.TestDataHref = FilePathToVfsUri(testDataFilePath, scmBaseUrl, hostConfig);
                response.TestData = await GetTestData(testDataFilePath, hostConfig);
            }

            if (!string.IsNullOrEmpty(functionMetadata.ScriptFile))
            {
                string scriptFilePath = Path.Combine(hostConfig.RootScriptPath, functionMetadata.ScriptFile);
                response.ScriptHref = FilePathToVfsUri(scriptFilePath, scmBaseUrl, hostConfig);
            }

            return response;
        }

        /// <summary>
        /// This method converts a FunctionMetadata into a JObject
        /// the scale controller understands. It's mainly the trigger binding
        /// with functionName inserted in it.
        /// </summary>
        /// <param name="functionMetadata">FunctionMetadata object to convert to a JObject.</param>
        /// <param name="config">ScriptHostConfiguration to read RootScriptPath from.</param>
        /// <returns>JObject that represent the trigger for scale controller to consume</returns>
        public static async Task<JObject> ToFunctionTrigger(this FunctionMetadata functionMetadata, ScriptHostConfiguration config)
        {
            // Only look at the function if it's not disabled
            if (!functionMetadata.IsDisabled)
            {
                // Get function.json path
                var functionPath = Path.Combine(config.RootScriptPath, functionMetadata.Name);
                var functionMetadataFilePath = Path.Combine(functionPath, ScriptConstants.FunctionMetadataFileName);

                // Read function.json as a JObject
                var functionConfig = await GetFunctionConfig(functionMetadataFilePath);

                // Find the trigger and add functionName to it
                foreach (JObject binding in (JArray)functionConfig["bindings"])
                {
                    var type = (string)binding["type"];
                    if (type.EndsWith("Trigger", StringComparison.OrdinalIgnoreCase))
                    {
                        binding.Add("functionName", functionMetadata.Name);
                        return binding;
                    }
                }
            }

            // If the function is disabled or has no trigger return null
            return null;
        }

        public static string GetTestDataFilePath(this FunctionMetadata functionMetadata, ScriptHostConfiguration config) =>
            GetTestDataFilePath(functionMetadata.Name, config);

        public static string GetTestDataFilePath(string functionName, ScriptHostConfiguration config) =>
            Path.Combine(config.TestDataPath, $"{functionName}.dat");

        private static async Task<JObject> GetFunctionConfig(string path)
        {
            try
            {
                if (FileUtility.FileExists(path))
                {
                    return JObject.Parse(await FileUtility.ReadAsync(path));
                }
            }
            catch
            {
                // no-op
            }

            // If there are any errors parsing function.json return an empty object.
            // This is current kudu behavior.
            // TODO: add an error field that can be used to communicate the JSON parse error.
            return new JObject();
        }

        private static async Task<string> GetTestData(string testDataPath, ScriptHostConfiguration config)
        {
            if (!File.Exists(testDataPath))
            {
                FileUtility.EnsureDirectoryExists(Path.GetDirectoryName(testDataPath));
                await FileUtility.WriteAsync(testDataPath, string.Empty);
            }

            return await FileUtility.ReadAsync(testDataPath);
        }

        internal static Uri GetFunctionInvokeUrlTemplate(string baseUrl, FunctionMetadata functionMetadata, string routePrefix)
        {
            var httpBinding = functionMetadata.InputBindings.FirstOrDefault(p => string.Compare(p.Type, "httpTrigger", StringComparison.OrdinalIgnoreCase) == 0);

            if (httpBinding != null)
            {
                string customRoute = null;
                if (httpBinding.Raw != null && httpBinding.Raw.TryGetValue("route", StringComparison.OrdinalIgnoreCase, out JToken value))
                {
                    // a custom route is specified
                    customRoute = (string)value;
                }

                string uriString = baseUrl.TrimEnd('/');
                if (!string.IsNullOrEmpty(routePrefix))
                {
                    uriString += $"/{routePrefix.TrimEnd('/')}";
                }

                if (!string.IsNullOrEmpty(customRoute))
                {
                    uriString += $"/{customRoute}";
                }
                else
                {
                    uriString += $"/{functionMetadata.Name}";
                }

                return new Uri(uriString.ToLowerInvariant());
            }

            return null;
        }

        internal static string GetScmBaseUrl()
        {
            // VFS APIs aren't exposed by the v1 runtime, so we must redirect
            // to the SCM site
            string hostName = HostNameProvider.Value;

            string scmHostName = null;
            if (!string.IsNullOrEmpty(hostName))
            {
                int idx = hostName.IndexOf('.');
                if (idx > 0)
                {
                    scmHostName = hostName.Insert(idx, ".scm");
                }
                else
                {
                    scmHostName = hostName + ".scm";
                }
            }
            else
            {
                scmHostName = "localhost";
            }

            return $"https://{scmHostName}";
        }

        internal static string GetAppBaseUrl()
        {
            string hostName = HostNameProvider.Value ?? "localhost";
            return $"https://{hostName}";
        }

        private static Uri GetFunctionHref(string functionName, string scmBaseUrl)
        {
            // this is the SCM route to the function
            return new Uri($"{scmBaseUrl}/api/functions/{functionName}");
        }

        internal static Uri FilePathToVfsUri(string filePath, string scmBaseUrl, ScriptHostConfiguration config, bool isDirectory = false)
        {
            var home = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath) ?? config.RootScriptPath;

            filePath = filePath
                .Substring(home.Length)
                .Trim('\\', '/')
                .Replace("\\", "/");

            // SCM VFS API route
            return new Uri($"{scmBaseUrl}/api/vfs/{filePath}{(isDirectory ? "/" : string.Empty)}");
        }
    }
}