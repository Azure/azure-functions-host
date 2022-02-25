// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class FunctionsHostingConfiguration : IFunctionsHostingConfiguration
    {
        private readonly TimeSpan _updateInterval = TimeSpan.FromMinutes(10);
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly string _configFilePath;

        private DateTime _configTTL = DateTime.UtcNow.AddMinutes(10); // next sync will be in 10 minutes
        private Dictionary<string, string> _configs;

        public FunctionsHostingConfiguration(IEnvironment environment, ILoggerFactory loggerFactory)
        {
            _environment = environment;
            _logger = loggerFactory.CreateLogger<FunctionsHostingConfiguration>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            _configFilePath = environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsRuntimeConfigFilePath);
            if (string.IsNullOrEmpty(_configFilePath))
            {
                _configFilePath = Environment.ExpandEnvironmentVariables(Path.Combine("%ProgramFiles(x86)%", "SiteExtensions", "kudu", "ScmHostingConfigurations.txt"));
            }
        }

        // For tests
        internal FunctionsHostingConfiguration(IEnvironment environment, ILoggerFactory loggerFactory, string configsFile, DateTime configsTTL, TimeSpan updateInterval)
            : this(environment, loggerFactory)
        {
            _configFilePath = configsFile;
            _configTTL = configsTTL;
            _updateInterval = updateInterval;
        }

        // for tests
        internal Dictionary<string, string> Config => _configs;

        public bool FunctionsWorkerDynamicConcurrencyEnabled
        {
            get
            {
                // Worker concurrecy feature can be enabled for whole stamp or for individual apps.
                string value = GetValue(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled, null);
                if (!string.IsNullOrEmpty(value))
                {
                    var values = value.Split(";").Select(x => x.ToLower());
                    string siteName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName);
                    return values.Contains("stamp") || values.Contains(siteName);
                }
                return false;
            }
        }

        public string GetValue(string key, string defaultValue = null)
        {
            var configs = _configs;
            if (configs == null || DateTime.UtcNow > _configTTL)
            {
                lock (_configFilePath)
                {
                    _configTTL = DateTime.UtcNow.Add(_updateInterval);

                    if (FileUtility.FileExists(_configFilePath))
                    {
                        var settings = FileUtility.ReadAllText(_configFilePath);
                        configs = Parse(settings);
                        _configs = configs;
                        _logger.LogInformation($"Updaiting FunctionsHostingConfigurations '{settings}'");
                    }
                    else
                    {
                        throw new InvalidOperationException("FunctionsHostingConfigurations does not exists");
                    }
                }
            }

            return (configs == null || !configs.TryGetValue(key, out string value)) ? defaultValue : value;
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
