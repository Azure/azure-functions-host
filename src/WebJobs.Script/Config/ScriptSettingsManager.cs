// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ScriptSettingsManager
    {
        private static ScriptSettingsManager _instance = new ScriptSettingsManager();
        private readonly ConcurrentDictionary<string, string> _settingsCache = new ConcurrentDictionary<string, string>();
        private Func<IConfiguration> _configurationFactory = BuildConfiguration;
        private Lazy<IConfiguration> _configuration = new Lazy<IConfiguration>(BuildConfiguration);

        // for testing
        public ScriptSettingsManager()
        {
        }

        /// <summary>
        /// Gets the underlying configuration object used by this instance of the <see cref="ScriptSettingsManager"/>.
        /// </summary>
        internal IConfiguration Configuration => _configuration.Value;

        public static ScriptSettingsManager Instance
        {
            get { return _instance; }
            set { _instance = value; }
        }

        public virtual bool IsAzureEnvironment => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId));

        public bool IsRemoteDebuggingEnabled => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.RemoteDebuggingPort));

        public virtual bool IsZipDeployment => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.AzureWebsiteZipDeployment));

        public virtual bool ContainerReady => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady));

        public string WebsiteSku => GetSetting(EnvironmentSettingNames.AzureWebsiteSku);

        public bool IsDynamicSku => WebsiteSku == ScriptConstants.DynamicSku;

        public virtual bool FileSystemIsReadOnly => IsZipDeployment;

        public virtual string AzureWebsiteDefaultSubdomain
        {
            get
            {
                return _settingsCache.GetOrAdd(nameof(AzureWebsiteDefaultSubdomain), k =>
                {
                    string siteHostName = GetSetting(EnvironmentSettingNames.AzureWebsiteHostName);

                    int? periodIndex = siteHostName?.IndexOf('.');

                    if (periodIndex != null && periodIndex > 0)
                    {
                        return siteHostName.Substring(0, periodIndex.Value);
                    }

                    return null;
                });
            }
        }

        /// <summary>
        /// Gets a value that uniquely identifies the site and slot.
        /// </summary>
        public virtual string AzureWebsiteUniqueSlotName
        {
            get
            {
                string name = GetSetting(EnvironmentSettingNames.AzureWebsiteName);
                string slotName = GetSetting(EnvironmentSettingNames.AzureWebsiteSlotName);

                if (!string.IsNullOrEmpty(slotName) &&
                    !string.Equals(slotName, ScriptConstants.DefaultProductionSlotName, StringComparison.OrdinalIgnoreCase))
                {
                    name += $"-{slotName}";
                }

                return name?.ToLowerInvariant();
            }
        }

        public virtual string InstanceId
         {
             get
             {
                 string instanceId = GetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId)
                     ?? Environment.MachineName.GetHashCode().ToString("X").PadLeft(32, '0');

                 return instanceId.Substring(0, Math.Min(instanceId.Length, 32));
             }
         }

        public virtual string ApplicationInsightsInstrumentationKey
        {
            get => GetSettingFromCache(EnvironmentSettingNames.AppInsightsInstrumentationKey);
            set => UpdateSettingInCache(EnvironmentSettingNames.AppInsightsInstrumentationKey, value);
        }

        public void SetConfigurationFactory(Func<IConfiguration> configurationRootFactory)
        {
            _configurationFactory = configurationRootFactory;
            Reset();
        }

        private string GetSettingFromCache(string settingKey)
        {
            if (string.IsNullOrEmpty(settingKey))
            {
                throw new ArgumentNullException(nameof(settingKey));
            }

            return _settingsCache.GetOrAdd(settingKey, (key) => GetSetting(key));
        }

        private void UpdateSettingInCache(string settingKey, string settingValue)
        {
            if (string.IsNullOrEmpty(settingKey))
            {
                throw new ArgumentNullException(nameof(settingKey));
            }

            _settingsCache.AddOrUpdate(settingKey, settingValue, (a, b) => settingValue);
        }

        public virtual void Reset()
        {
            _configuration = new Lazy<IConfiguration>(_configurationFactory);

            _settingsCache.Clear();
        }

        public virtual string GetSetting(string settingKey)
        {
            if (string.IsNullOrEmpty(settingKey))
            {
                return null;
            }

            return Configuration[settingKey];
        }

        public virtual void SetSetting(string settingKey, string settingValue)
        {
            if (!string.IsNullOrEmpty(settingKey))
            {
                Environment.SetEnvironmentVariable(settingKey, settingValue);
                Reset();
            }
        }

        private static IConfigurationRoot BuildConfiguration()
        {
            var configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables();

            return configurationBuilder.Build();
        }
    }
}