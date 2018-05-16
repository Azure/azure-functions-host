// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ScriptSettingsManager
    {
        private static ScriptSettingsManager _instance = new ScriptSettingsManager();

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

        public virtual bool ContainerReady => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady));

        public string WebsiteSku => GetSetting(EnvironmentSettingNames.AzureWebsiteSku);

        public bool IsDynamicSku => WebsiteSku == ScriptConstants.DynamicSku;

        public virtual bool FileSystemIsReadOnly => IsZipDeployment;

        public virtual string AzureWebsiteDefaultSubdomain
        {
            get
            {
                string siteHostName = GetSetting(EnvironmentSettingNames.AzureWebsiteHostName);

                int? periodIndex = siteHostName?.IndexOf('.');

                if (periodIndex != null && periodIndex > 0)
                {
                    return siteHostName.Substring(0, periodIndex.Value);
                }

                return null;
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
            get => Utility.GetSettingFromConfigOrEnvironment(EnvironmentSettingNames.AppInsightsInstrumentationKey);
            set => SetSetting(EnvironmentSettingNames.AppInsightsInstrumentationKey, value);
        }

        public virtual string GetSetting(string settingKey)
        {
            if (string.IsNullOrEmpty(settingKey))
            {
                throw new ArgumentNullException(nameof(settingKey));
            }

            return Environment.GetEnvironmentVariable(settingKey);
        }

        public virtual void SetSetting(string settingKey, string settingValue)
        {
            if (string.IsNullOrEmpty(settingKey))
            {
                throw new ArgumentNullException(nameof(settingKey));
            }

            Environment.SetEnvironmentVariable(settingKey, settingValue);
        }
    }
}