// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal sealed class WorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly IWorkerProfileManager _profileManager;
        private readonly IEnvironment _environment;
        private readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public WorkerConfigurationResolver(IConfiguration config,
                                        ILogger logger,
                                        IEnvironment environment,
                                        IWorkerProfileManager workerProfileManager)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _profileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
        }

        public List<string> GetWorkerConfigs(List<string> probingPaths, string fallbackPath)
        {
            var workerRuntime = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);

            // Dictionary of language-name : workerConfig
            ConcurrentDictionary<string, string> outputDict = new ConcurrentDictionary<string, string>();

            // check worker release channel
            string releaseChannel = Utility.GetPlatformReleaseChannel(_environment);

            foreach (var probingPath in probingPaths)
            {
                foreach (var languageWorkerPath in Directory.EnumerateDirectories(probingPath))
                {
                    string languageWorkerFolder = Path.GetFileName(languageWorkerPath);

                    //if not dotnet-isolated -- it will always be read from fallback path
                    if (!string.IsNullOrWhiteSpace(workerRuntime) &&
                        !workerRuntime.Equals(RpcWorkerConstants.DotNetIsolatedLanguageWorkerName, StringComparison.OrdinalIgnoreCase) &&
                        !_environment.IsPlaceholderModeEnabled() &&
                        !_environment.IsMultiLanguageRuntimeEnvironment())
                    {
                        // Only skip worker directories that don't match the current runtime.
                        // Do not skip non-worker directories like the function app payload directory
                        if (!workerRuntime.Equals(languageWorkerFolder, StringComparison.OrdinalIgnoreCase) && languageWorkerPath.StartsWith(fallbackPath))
                        {
                            continue;
                        }
                    }

                    IEnumerable<string> workerVersions = Directory.EnumerateDirectories(languageWorkerPath);
                    var versions = ParseWorkerVersions(workerVersions);
                    versions.OrderDescending();

                    int found = 0;

                    if (outputDict.ContainsKey(languageWorkerFolder))
                    {
                        continue;
                    }

                    // language worker version
                    foreach (Version versionFolder in versions)
                    {
                        if (IsCompatibleWithHost(Path.Combine(languageWorkerPath, versionFolder.ToString())))
                        {
                            found++;
                            outputDict[languageWorkerFolder] = languageWorkerPath;

                            if (string.IsNullOrEmpty(releaseChannel) || !releaseChannel.Equals(ScriptConstants.StandardPlatformChannelNameUpper))
                            {
                                // latest version is the default
                                break;
                            }

                            if (found > 1 && releaseChannel.Equals(ScriptConstants.StandardPlatformChannelNameUpper))
                            {
                                outputDict[languageWorkerFolder] = languageWorkerPath;
                                break;
                            }
                        }
                    }
                }
            }

            // fallback path

            foreach (var workerDir in Directory.EnumerateDirectories(fallbackPath))
            {
                string workerFolder = Path.GetFileName(workerDir);

                if (outputDict.ContainsKey(workerFolder))
                {
                    continue;
                }

                string workerConfigPath = Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);
                if (File.Exists(workerConfigPath))
                {
                    outputDict[workerFolder] = workerDir;
                }
            }

            return outputDict.Values.ToList();
        }

        internal List<Version> ParseWorkerVersions(IEnumerable<string> workerVersions)
        {
            var versions = new List<Version>();

            foreach (var workerVersion in workerVersions)
            {
                string versionFolder = Path.GetFileName(workerVersion);

                if (Version.TryParse(versionFolder, out Version version))
                {
                    versions.Add(version);
                }
            }

            return versions;
        }

        internal HashSet<string> GetHostCapabilities()
        {
            HashSet<string> hostCapabilites = ["test-capability-1", "test-capability-2"];

            return hostCapabilites;
        }

        internal bool IsCompatibleWithHost(string workerDir)
        {
            string workerConfigPath = Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);
            if (!File.Exists(workerConfigPath))
            {
                return false;
            }

            JsonElement workerConfig = WorkerConfigurationHelper.GetWorkerConfigJsonElement(workerConfigPath);

            // static capability resolution
            bool doesHostRequirementMeet = DoesHostRequirementMeet(workerConfig);

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

        internal HashSet<string> GetHostRequirements(JsonElement workerConfig)
        {
            HashSet<string> hostRequirements = new HashSet<string>();

            if (workerConfig.TryGetProperty("hostRequirements", out JsonElement configSection))
            {
                var requirements = configSection.EnumerateArray();

                foreach (var requirement in requirements)
                {
                    hostRequirements.Add(requirement.GetString());
                }
            }

            return hostRequirements;
        }

        internal bool DoesHostRequirementMeet(JsonElement workerConfig)
        {
            HashSet<string> hostRequirements = GetHostRequirements(workerConfig);

            HashSet<string> hostCapabilities = GetHostCapabilities();

            foreach (var hostRequirement in hostRequirements)
            {
                if (!hostCapabilities.Contains(hostRequirement))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
