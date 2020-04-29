﻿// Copyright (c) .NET Foundation. All rights reserved.
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

        public virtual bool IsCoreToolsEnvironment => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.CoreToolsEnvironment));

        public virtual bool ConfigurationReady => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.AzureWebsiteConfigurationReady));

        public string WebsiteSku => GetSetting(EnvironmentSettingNames.AzureWebsiteSku);

        public bool IsDynamicSku => WebsiteSku == ScriptConstants.DynamicSku;

        public virtual bool FileSystemIsReadOnly => IsZipDeployment;

        /// <summary>
        /// Gets a value that uniquely identifies the site and slot.
        /// </summary>
        public virtual string AzureWebsiteUniqueSlotName
        {
            get
            {
                return Utility.GetWebsiteUniqueSlotName();
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
            return Utility.GetSetting(settingKey);
        }

        public string GetSettingOrDefault(string settingKey, string defaultValue)
        {
            return GetSetting(settingKey) ?? defaultValue;
        }

        public virtual void SetSetting(string settingKey, string settingValue)
        {
            if (string.IsNullOrEmpty(settingKey))
            {
                throw new ArgumentNullException(nameof(settingKey));
            }

            Environment.SetEnvironmentVariable(settingKey, settingValue);
        }

        public bool SettingIsEnabled(string settingKey)
        {
            string value = GetSetting(settingKey);
            if (!string.IsNullOrEmpty(value) &&
                (string.Compare(value, "1", StringComparison.OrdinalIgnoreCase) == 0 ||
                 string.Compare(value, "true", StringComparison.OrdinalIgnoreCase) == 0))
            {
                return true;
            }

            return false;
        }
    }
}