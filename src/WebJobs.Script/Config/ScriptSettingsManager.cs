// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ScriptSettingsManager
    {
        private static ScriptSettingsManager _instance = new ScriptSettingsManager();
        private readonly ConcurrentDictionary<string, string> _settingsCache = new ConcurrentDictionary<string, string>();

        // for testing
        public ScriptSettingsManager()
        {
        }

        public static ScriptSettingsManager Instance
        {
            get { return _instance; }
            set { _instance = value; }
        }

        public virtual bool IsAzureEnvironment => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId));

        public bool IsRemoteDebuggingEnabled => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.RemoteDebuggingPort));

        public virtual bool IsZipDeployment => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.AzureWebsiteZipDeployment));

        public bool IsDynamicSku => GetSetting(EnvironmentSettingNames.AzureWebsiteSku) == ScriptConstants.DynamicSku;

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

                 return instanceId.Substring(0, 32);
             }
         }

        public virtual string ApplicationInsightsInstrumentationKey
        {
            get => GetSettingFromCache(EnvironmentSettingNames.AppInsightsInstrumentationKey);
            set => UpdateSettingInCache(EnvironmentSettingNames.AppInsightsInstrumentationKey, value);
        }

        private string GetSettingFromCache(string settingKey)
        {
            if (string.IsNullOrEmpty(settingKey))
            {
                throw new ArgumentNullException(nameof(settingKey));
            }

            return _settingsCache.GetOrAdd(settingKey, (key) => Utility.GetSettingFromConfigOrEnvironment(key));
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
            _settingsCache.Clear();
        }

        public virtual string GetSetting(string settingKey)
        {
            string settingValue = null;
            if (!string.IsNullOrEmpty(settingKey))
            {
                settingValue = Environment.GetEnvironmentVariable(settingKey);
            }

            return settingValue;
        }

        public virtual void SetSetting(string settingKey, string settingValue)
        {
            if (!string.IsNullOrEmpty(settingKey))
            {
                Environment.SetEnvironmentVariable(settingKey, settingValue);
            }
        }
    }
}