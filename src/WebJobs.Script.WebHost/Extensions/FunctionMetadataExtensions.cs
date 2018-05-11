// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Management.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class FunctionMetadataExtensions
    {
        /// <summary>
        /// Maps FunctionMetadata to FunctionMetadataResponse.
        /// </summary>
        /// <param name="functionMetadata">FunctionMetadata to be mapped.</param>
        /// <param name="request">Current HttpRequest</param>
        /// <param name="config">ScriptHostConfig</param>
        /// <returns>Promise of a FunctionMetadataResponse</returns>
        public static async Task<FunctionMetadataResponse> ToFunctionMetadataResponse(this FunctionMetadata functionMetadata, HttpRequest request, ScriptHostConfiguration config)
        {
            var functionPath = Path.Combine(config.RootScriptPath, functionMetadata.Name);
            var functionMetadataFilePath = Path.Combine(functionPath, ScriptConstants.FunctionMetadataFileName);
            var baseUrl = request != null
                ? $"{request.Scheme}://{request.Host}"
                : "https://localhost/";

            var response = new FunctionMetadataResponse
            {
                Name = functionMetadata.Name,

                // Q: can functionMetadata.ScriptFile be null or empty?
                ScriptHref = VirtualFileSystem.FilePathToVfsUri(Path.Combine(config.RootScriptPath, functionMetadata.ScriptFile), baseUrl, config),
                ConfigHref = VirtualFileSystem.FilePathToVfsUri(functionMetadataFilePath, baseUrl, config),
                ScriptRootPathHref = VirtualFileSystem.FilePathToVfsUri(functionPath, baseUrl, config, isDirectory: true),
                TestDataHref = VirtualFileSystem.FilePathToVfsUri(functionMetadata.GetTestDataFilePath(config), baseUrl, config),
                Href = GetFunctionHref(functionMetadata.Name, baseUrl),
                TestData = await GetTestData(functionMetadata.GetTestDataFilePath(config), config),
                Config = await GetFunctionConfig(functionMetadataFilePath),

                // Properties below this comment are not present in the kudu version.
                IsDirect = functionMetadata.IsDirect,
                IsDisabled = functionMetadata.IsDisabled,
                IsProxy = functionMetadata.IsProxy
            };
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
                await FileUtility.WriteAsync(testDataPath, string.Empty);
            }

            return await FileUtility.ReadAsync(testDataPath);
        }

        private static Uri GetFunctionHref(string functionName, string baseUrl) =>
            new Uri($"{baseUrl}/admin/functions/{functionName}");
    }
}