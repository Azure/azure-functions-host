// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Authentication;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal static class WorkerConfigurationResolver
    {
        internal static List<string> GetWorkerConfigs(
            List<string> probingPaths,
            string fallbackPath,
            IEnvironment environment,
            JsonSerializerOptions _jsonSerializerOptions,
            IWorkerProfileManager _profileManager,
            IConfiguration _config,
            ILogger _logger
            )
        {
            // Dict of language-name : workerConfig
            ConcurrentDictionary<string, string> outputDict = new ConcurrentDictionary<string, string>();

            //if not dotnet-isolated -- it will always be read from fallback path

            // check worker release channel
            string releaseChannel = Utility.GetPlatformReleaseChannel(environment);

            HashSet<string> hostCapabilites = GetHostCapabilities();

            // test
            foreach (var probingPath in probingPaths)
            {
                // language worker
                foreach (var languageWorkerPath in Directory.EnumerateDirectories(probingPath))
                {
                    var workerVersions = Directory.EnumerateDirectories(languageWorkerPath);

                    var versions = new List<Version>();
                    foreach (var v in workerVersions)
                    {
                        string versionFolder = Path.GetFileName(v);
                        if (Version.TryParse(versionFolder, out Version version))
                        {
                            versions.Add(version);
                        }
                    }

                    versions.OrderDescending();

                    int found = 0;

                    if (outputDict.ContainsKey(languageWorkerPath))
                    {
                        continue;
                    }

                    // language worker version
                    foreach (var versionFolder in versions)
                    {
                        string workerConfigPath = Path.Combine(languageWorkerPath, versionFolder.ToString(), RpcWorkerConstants.WorkerConfigFileName);
                        if (File.Exists(workerConfigPath))
                        {
                            // static capability resolution
                            if (IsCompatibleWithHost(
                                hostCapabilites,
                                workerConfigPath,
                                _jsonSerializerOptions,
                                Path.Combine(languageWorkerPath, versionFolder.ToString()),
                                _profileManager,
                                _config,
                                _logger))
                            {
                                found++;
                                outputDict[languageWorkerPath] = workerConfigPath;

                                if (string.IsNullOrEmpty(releaseChannel) || !releaseChannel.Equals(ScriptConstants.StandardPlatformChannelNameUpper))
                                {
                                    // latest version is the default
                                    break;
                                }

                                if (found == 2 && releaseChannel.Equals(ScriptConstants.StandardPlatformChannelNameUpper))
                                {
                                    outputDict[languageWorkerPath] = workerConfigPath;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            // fallback path

            foreach (var workerDir in Directory.EnumerateDirectories(fallbackPath))
            {
                if (outputDict.ContainsKey(workerDir))
                {
                    continue;
                }

                string workerConfigPath = Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);
                if (File.Exists(workerConfigPath))
                {
                    outputDict[workerDir] = workerConfigPath;
                }
            }

            return outputDict.Values.ToList();
        }

        private static HashSet<string> GetHostCapabilities()
        {
            HashSet<string> hostCapabilites = ["test-capability-1", "test-capability-2"];

            return hostCapabilites;
        }

        private static bool IsCompatibleWithHost(
            HashSet<string> hostCapabilities,
            string workerConfigPath,
            JsonSerializerOptions _jsonSerializerOptions,
            string workerDir,
            IWorkerProfileManager _profileManager,
            IConfiguration _config,
            ILogger _logger)
        {
            var workerConfig = WorkerConfigurationHelper.GetWorkerConfigJsonElement(workerConfigPath);

            HashSet<string> n = new HashSet<string>();

            // Read worker config section = capabilities as HashSet
            var a = workerConfig.GetProperty("hostRequirements");

            var b = a.EnumerateArray();

            foreach (var k in b)
            {
                var m = k.GetString();
                n.Add(m);
            }

            foreach (var l in n)
            {
                if (!hostCapabilities.Contains(l))
                {
                    return false;
                }
            }

            // profiles evaluation

            RpcWorkerDescription workerDescription = WorkerConfigurationHelper.GetWorkerDescription(
                workerConfig,
                _jsonSerializerOptions,
                workerDir,
                _profileManager,
                _config,
                _logger);

            if (workerDescription.IsDisabled == true)
            {
                return false;
            }

            return true;
        }
    }
}
