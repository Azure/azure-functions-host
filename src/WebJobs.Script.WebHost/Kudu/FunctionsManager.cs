﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public class FunctionsManager : IFunctionsManager
    {
        private readonly IEnvironment _environment;

        public FunctionsManager(IEnvironment environment)
        {
            _environment = environment;
        }

        private string HostJsonPath
        {
            get
            {
                return Path.Combine(_environment.FunctionsPath, KuduConstants.FunctionsHostConfigFile);
            }
        }

        public async Task<FunctionEnvelope> CreateOrUpdateAsync(string name, FunctionEnvelope functionEnvelope)
        {
            var functionDir = Path.Combine(_environment.FunctionsPath, name);

            // Make sure the function folder exists
            FileSystemHelpers.EnsureDirectory(functionDir);

            string newConfig = null;
            string configPath = Path.Combine(functionDir, KuduConstants.FunctionsConfigFile);
            string dataFilePath = GetFunctionTestDataFilePath(name);

            // If files are included, write them out
            if (functionEnvelope?.Files != null)
            {
                // If the config is passed in the file collection, save it and don't process it as a file
                if (functionEnvelope.Files.TryGetValue(KuduConstants.FunctionsConfigFile, out newConfig))
                {
                    functionEnvelope.Files.Remove(KuduConstants.FunctionsConfigFile);
                }

                // Delete all existing files in the directory. This will also delete current function.json, but it gets recreated below
                FileSystemHelpers.DeleteDirectoryContentsSafe(functionDir);

                await Task.WhenAll(functionEnvelope.Files.Select(e => FileSystemHelpers.WriteAllTextToFileAsync(Path.Combine(functionDir, e.Key), e.Value)));
            }

            // Get the config (if it was not already passed in as a file)
            if (newConfig == null && functionEnvelope?.Config != null)
            {
                newConfig = JsonConvert.SerializeObject(functionEnvelope?.Config, Formatting.Indented);
            }

            // Get the current config, if any
            string currentConfig = null;
            if (FileSystemHelpers.FileExists(configPath))
            {
                currentConfig = await FileSystemHelpers.ReadAllTextFromFileAsync(configPath);
            }

            // Save the file and set changed flag is it has changed. This helps optimize the syncTriggers call
            if (newConfig != currentConfig)
            {
                await FileSystemHelpers.WriteAllTextToFileAsync(configPath, newConfig);
            }

            if (functionEnvelope.TestData != null)
            {
                await FileSystemHelpers.WriteAllTextToFileAsync(dataFilePath, functionEnvelope.TestData);
            }

            return await GetFunctionConfigAsync(name);
        }

        public async Task<IEnumerable<FunctionEnvelope>> ListFunctionsConfigAsync()
        {
            var configList = await Task.WhenAll(
                    FileSystemHelpers
                    .GetDirectories(_environment.FunctionsPath)
                    .Select(d => Path.Combine(d, KuduConstants.FunctionsConfigFile))
                    .Where(FileSystemHelpers.FileExists)
                    .Select(f => TryGetFunctionConfigAsync(Path.GetFileName(Path.GetDirectoryName(f)))));
            return configList.Where(c => c != null);
        }

        public async Task<FunctionEnvelope> GetFunctionConfigAsync(string name)
        {
            var config = await TryGetFunctionConfigAsync(name);
            if (config == null)
            {
                throw new FileNotFoundException($"Function ({name}) config does not exist or is invalid");
            }
            return config;
        }

        public async Task<FunctionSecrets> GetFunctionSecretsAsync(string functionName)
        {
            FunctionSecrets secrets;
            string secretFilePath = GetFunctionSecretsFilePath(functionName);
            if (FileSystemHelpers.FileExists(secretFilePath))
            {
                // load the secrets file
                string secretsJson = await FileSystemHelpers.ReadAllTextFromFileAsync(secretFilePath);
                secrets = JsonConvert.DeserializeObject<FunctionSecrets>(secretsJson);
            }
            else
            {
                // initialize with new secrets and save it
                secrets = new FunctionSecrets
                {
                    Key = SecurityUtility.GenerateSecretString()
                };

                FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(secretFilePath));
                await FileSystemHelpers.WriteAllTextToFileAsync(secretFilePath, JsonConvert.SerializeObject(secrets, Formatting.Indented));
            }

            secrets.TriggerUrl = String.Format(@"https://{0}/api/{1}?code={2}",
                System.Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME") ?? "localhost",
                functionName,
                secrets.Key);

            return secrets;
        }

        public async Task<JObject> GetHostConfigAsync()
        {
            var host = await TryGetHostConfigAsync();
            if (host == null)
            {
                throw new FileNotFoundException("Host.json is invalid");
            }
            return host;
        }

        private async Task<JObject> TryGetHostConfigAsync()
        {
            try
            {
                return FileSystemHelpers.FileExists(HostJsonPath)
                    ? JObject.Parse(await FileSystemHelpers.ReadAllTextFromFileAsync(HostJsonPath))
                    : new JObject();
            }
            catch (Exception e)
            {
                // no-op
                Console.WriteLine(e);
            }

            return null;
        }

        public async Task<JObject> PutHostConfigAsync(JObject content)
        {
            await FileSystemHelpers.WriteAllTextToFileAsync(HostJsonPath, JsonConvert.SerializeObject(content));
            return await GetHostConfigAsync();
        }

        public void DeleteFunction(string name)
        {
            FileSystemHelpers.DeleteDirectorySafe(GetFunctionPath(name), ignoreErrors: false);
            FileSystemHelpers.DeleteFileSafe(GetFunctionTestDataFilePath(name));
            FileSystemHelpers.DeleteFileSafe(GetFunctionSecretsFilePath(name));
            FileSystemHelpers.DeleteFileSafe(GetFunctionLogPath(name));
        }

        private async Task<FunctionEnvelope> TryGetFunctionConfigAsync(string name)
        {
            try
            {
                var path = GetFunctionConfigPath(name);
                if (FileSystemHelpers.FileExists(path))
                {
                    return CreateFunctionConfig(await FileSystemHelpers.ReadAllTextFromFileAsync(path), name);
                }
            }
            catch
            {
                // no-op
            }
            return null;
        }

        private FunctionEnvelope CreateFunctionConfig(string configContent, string functionName)
        {
            var config = JObject.Parse(configContent);
            return new FunctionEnvelope
            {
                Name = functionName,
                ScriptRootPathHref = FilePathToVfsUri(GetFunctionPath(functionName), isDirectory: true),
                ScriptHref = FilePathToVfsUri(GetFunctionScriptPath(functionName, config)),
                ConfigHref = FilePathToVfsUri(GetFunctionConfigPath(functionName)),
                SecretsFileHref = FilePathToVfsUri(GetFunctionSecretsFilePath(functionName)),
                Href = GetFunctionHref(functionName),
                Config = config,
                TestData = GetFunctionTestData(functionName)
            };
        }

        // Logic for this function is copied from here
        // https://github.com/Azure/azure-webjobs-sdk-script/blob/e0a783e882dd8680bf23e3c8818fb9638071c21d/src/WebJobs.Script/Config/ScriptHost.cs#L113-L150
        private string GetFunctionScriptPath(string functionName, JObject functionInfo)
        {
            var functionPath = GetFunctionPath(functionName);
            var functionFiles = FileSystemHelpers.GetFiles(functionPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(p => Path.GetFileName(p).ToLowerInvariant() != "function.json").ToArray();

            if (functionFiles.Length == 0)
            {
                return functionPath;
            }
            else if (functionFiles.Length == 1)
            {
                // if there is only a single file, that file is primary
                return functionFiles[0];
            }
            else
            {
                // if there is a "run" file, that file is primary
                string functionPrimary = null;
                functionPrimary = functionFiles.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant() == "run");
                if (string.IsNullOrEmpty(functionPrimary))
                {
                    // for Node, any index.js file is primary
                    functionPrimary = functionFiles.FirstOrDefault(p => Path.GetFileName(p).ToLowerInvariant() == "index.js");
                    if (string.IsNullOrEmpty(functionPrimary))
                    {
                        // finally, if there is an explicit primary file indicated
                        // in config, use it
                        JToken token = functionInfo["source"];
                        if (token != null)
                        {
                            string sourceFileName = (string)token;
                            functionPrimary = Path.Combine(functionPath, sourceFileName);
                        }
                    }
                }

                if (string.IsNullOrEmpty(functionPrimary))
                {
                    // TODO: should this be an error?
                    return functionPath;
                }
                return functionPrimary;
            }
        }

        private Uri FilePathToVfsUri(string filePath, bool isDirectory = false)
        {
            if (filePath.IndexOf(_environment.RootPath, StringComparison.OrdinalIgnoreCase) != -1)
            {
                filePath = filePath.Substring(_environment.RootPath.Length);
            }
            else
            {
                filePath = filePath.Replace(VfsSpecialFolders.SystemDrivePath, VfsSpecialFolders.SystemDriveFolder);
            }

            return new Uri($"{_environment.AppBaseUrlPrefix}/api/vfs/{filePath.Trim('\\').Replace("\\", "/")}{(isDirectory ? "/" : string.Empty)}");
        }

        private Uri GetFunctionHref(string functionName)
        {
            return new Uri($"{_environment.AppBaseUrlPrefix}/api/functions/{functionName}");
        }

        private string GetFunctionPath(string name)
        {
            var path = Path.Combine(_environment.FunctionsPath, name);
            if (FileSystemHelpers.DirectoryExists(path))
            {
                return path;
            }

            throw new FileNotFoundException($"Function ({name}) does not exist");
        }

        private string GetFunctionConfigPath(string name)
        {
            return Path.Combine(GetFunctionPath(name), KuduConstants.FunctionsConfigFile);
        }

        private string GetFunctionLogPath(string name)
        {
            return Path.Combine(_environment.ApplicationLogFilesPath, KuduConstants.Functions, KuduConstants.Function, name);
        }

        private string GetFunctionTestData(string functionName)
        {
            string testDataFilePath = GetFunctionTestDataFilePath(functionName);

            // Create an empty file if it doesn't exist
            if (!FileSystemHelpers.FileExists(testDataFilePath))
            {
                FileSystemHelpers.WriteAllText(testDataFilePath, String.Empty);
            }

            return FileSystemHelpers.ReadAllText(testDataFilePath);
        }

        private string GetFunctionTestDataFilePath(string functionName)
        {
            string folder = Path.Combine(_environment.DataPath, KuduConstants.Functions, KuduConstants.SampleData);
            FileSystemHelpers.EnsureDirectory(folder);
            return Path.Combine(folder, $"{functionName}.dat");
        }

        private string GetFunctionSecretsFilePath(string functionName)
        {
            return Path.Combine(_environment.DataPath, KuduConstants.Functions, KuduConstants.Secrets, $"{functionName}.json");
        }
    }
}