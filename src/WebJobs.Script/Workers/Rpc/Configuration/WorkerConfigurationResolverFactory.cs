// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    /// <summary>
    /// Factory for creating worker configuration resolvers depending on if dynamic worker resolution is enabled or not.
    /// </summary>
    internal sealed class WorkerConfigurationResolverFactory : IWorkerConfigurationResolverFactory
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly IWorkerProfileManager _workerProfileManager;
        private readonly IOptions<FunctionsHostingConfigOptions> _functionsHostingConfigOptions;

        public WorkerConfigurationResolverFactory(
                    IConfiguration configuration,
                    ILogger logger, IEnvironment environment,
                    IWorkerProfileManager workerProfileManager,
                    IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _workerProfileManager = workerProfileManager ?? throw new ArgumentNullException(nameof(workerProfileManager));
            _functionsHostingConfigOptions = functionsHostingConfigOptions ?? throw new ArgumentNullException(nameof(functionsHostingConfigOptions));
        }

        public IWorkerConfigurationResolver CreateResolver()
        {
            HashSet<string> workersAvailableForResolution = GetWorkersAvailableForResolutionViaHostingConfig(_functionsHostingConfigOptions);
            List<string> probingPaths = GetWorkerProbingPaths();
            bool dynamicWorkerResolutionEnabled = _environment.IsDynamicWorkerResolutionEnabled(workersAvailableForResolution);

            if (dynamicWorkerResolutionEnabled)
            {
                return new DynamicWorkerConfigurationResolver(_configuration,
                                                                _logger,
                                                                _environment,
                                                                FileUtility.Instance,
                                                                _workerProfileManager,
                                                                workersAvailableForResolution,
                                                                probingPaths);
            }

            return new DefaultWorkerConfigurationResolver(_configuration, _logger);
        }

        internal List<string> GetWorkerProbingPaths()
        {
            // If Configuration section is set, read probing paths from configuration.
            IConfigurationSection probingPathsSection = _configuration.GetSection($"{RpcWorkerConstants.LanguageWorkersSectionName}")
                                                                ?.GetSection($"{RpcWorkerConstants.WorkerProbingPathsSectionName}");

            var probingPathsList = probingPathsSection?.AsEnumerable();

            List<string> probingPaths = new List<string>();

            if (probingPathsList is not null)
            {
                for (int i = 0; i < probingPathsList.Count(); i++)
                {
                    var path = probingPathsSection.GetSection($"{i}").Value;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        probingPaths.Add(path);
                    }
                }
            }
            else
            {
                if (_environment.IsWindowsEnvironment())
                {
                    // Harcoded site extensions path for Windows until Antares sets it as an Environment variable.
                    // Example probing path for Windows: "c:\\home\\SiteExtensions\\workers"
                    string windowsSiteExtensionsPath = GetWindowsSiteExtensionsPath();

                    if (!string.IsNullOrWhiteSpace(windowsSiteExtensionsPath))
                    {
                        var windowsWorkerFullProbingPath = Path.Combine(windowsSiteExtensionsPath, RpcWorkerConstants.DefaultWorkersDirectoryName);
                        probingPaths.Add(windowsWorkerFullProbingPath);
                    }
                }
            }

            return probingPaths;
        }

        internal static HashSet<string> GetWorkersAvailableForResolutionViaHostingConfig(IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions) =>
            (functionsHostingConfigOptions.Value?.WorkersAvailableForDynamicResolution ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        internal static string GetWindowsSiteExtensionsPath()
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var assemblyDir = Path.GetDirectoryName(assemblyPath);

            //Move 2 directories up to get to the SiteExtensions directory
            return Directory.GetParent(assemblyDir)?.Parent?.FullName;
        }
    }
}