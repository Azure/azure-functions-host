// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class FunctionsHostingConfiguration : IFunctionsHostingConfiguration
    {
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly string _configFilePath;
        private bool _isInitialized = false;
        private Dictionary<string, string> _configs;

        public FunctionsHostingConfiguration(IEnvironment environment, ILoggerFactory loggerFactory)
        {
            _environment = environment;
            _logger = loggerFactory.CreateLogger<FunctionsHostingConfiguration>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            _configFilePath = environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsPlatformConfigFilePath);
            if (string.IsNullOrEmpty(_configFilePath))
            {
                _configFilePath = Environment.ExpandEnvironmentVariables(Path.Combine("%ProgramFiles(x86)%", "SiteExtensions", "kudu", "ScmHostingConfigurations.txt"));
            }

            Utility.ExecuteAfterColdStartDelay(_environment, () =>
            {
                lock (_configFilePath)
                {
                    if (!_isInitialized)
                    {
                        if (FileUtility.FileExists(_configFilePath))
                        {
                            var settings = FileUtility.ReadAllText(_configFilePath);
                            _configs = Parse(settings);
                            _logger.LogDebug($"Loading FunctionsHostingConfiguration '{settings}'.");
                        }
                        else
                        {
                            _configs = new Dictionary<string, string>();
                            _logger.LogDebug($"FunctionsHostingConfiguration file does not exist.");
                        }
                        _isInitialized = true;
                        Initialized?.Invoke(this, EventArgs.Empty);
                    }
                }
            });
        }

        // For tests
        internal FunctionsHostingConfiguration(IEnvironment environment, ILoggerFactory loggerFactory, string configsFile)
            : this(environment, loggerFactory)
        {
            _configFilePath = configsFile;
        }

        public event EventHandler Initialized;

        public bool IsInitialized => _isInitialized;

        public IDictionary<string, string> GetFeatureFlags()
        {
            if (!_isInitialized)
            {
                _logger.LogWarning($"FunctionsHostingConfigurations haven't initialized initialized");
                return null;
            }

            return new Dictionary<string, string>(_configs);
        }

        public string GetFeatureFlag(string key)
        {
            if (!_isInitialized)
            {
                _logger.LogWarning($"FunctionsHostingConfigurations haven't initialized on getting '{key}'");
                return null;
            }

            _configs.TryGetValue(key, out string value);
            return value;
        }

        private static Dictionary<string, string> Parse(string settings)
        {
            // Expected settings: "ENABLE_FEATUREX=1,A=B,TimeOut=123"
            return string.IsNullOrEmpty(settings)
                ? new Dictionary<string, string>()
                : settings
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries))
                    .Where(a => a.Length == 2)
                    .ToDictionary(a => a[0], a => a[1], StringComparer.OrdinalIgnoreCase);
        }
    }
}
