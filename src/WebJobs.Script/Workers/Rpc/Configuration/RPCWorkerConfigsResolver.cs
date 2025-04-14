// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal static class RPCWorkerConfigsResolver
    {
        internal static List<string> GetWorkerConfigs(List<string> probingPaths, string fallbackPath)
        {
            bool featureFlagEnabled = IsFeatureFlagEnabled();

            // Dict of language-name : workerConfig
            ConcurrentDictionary<string, string> outputDict = new ConcurrentDictionary<string, string>();

            if (featureFlagEnabled)
            {
                // check workerPinning
                string requiredWorkerVersion = GetPinnedWorkerVersionOrDefault();

                HashSet<string> hostCapabilites = GetHostCapabilities();

                // test
                foreach (var probingPath in probingPaths)
                {
                    // language worker
                    foreach (var languageWorkerPath in Directory.EnumerateDirectories(probingPath))
                    {
                        var workerVersions = Directory.EnumerateDirectories(languageWorkerPath);
                        workerVersions.OrderDescending();

                        int found = 0;

                        if (outputDict.ContainsKey(languageWorkerPath))
                        {
                            continue;
                        }

                        // language worker version
                        foreach (var versionFolder in workerVersions)
                        {
                            string workerConfigPath = Path.Combine(versionFolder, RpcWorkerConstants.WorkerConfigFileName);
                            if (File.Exists(workerConfigPath))
                            {
                                // static capability resolution
                                if (IsCompatibleWithHost(hostCapabilites, workerConfigPath))
                                {
                                    found++;
                                    outputDict[languageWorkerPath] = workerConfigPath;

                                    if (requiredWorkerVersion.Equals("latest"))
                                    {
                                        break;
                                    }

                                    if (found == 2 && requiredWorkerVersion.Equals("standard"))
                                    {
                                        outputDict[languageWorkerPath] = workerConfigPath;
                                        break;
                                    }
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

            return (List<string>)outputDict.Values;
        }

        private static string GetPinnedWorkerVersionOrDefault()
        {
            return "latest"; // or "standard"
        }

        private static HashSet<string> GetHostCapabilities()
        {
            HashSet<string> hostCapabilites = ["test-capability-1", "test-capability-2"];

            return hostCapabilites;
        }

        private static bool IsCompatibleWithHost(HashSet<string> hostCapabilities, string workerConfigPath)
        {
            // Read worker config section = capabilities as HashSet
            // for each capability in Host, should exist in worker.
            // what about extra capability in worker?

            return true;
        }

        private static bool IsFeatureFlagEnabled()
        {
            return true;
        }
    }
}
