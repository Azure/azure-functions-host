// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ScriptSettingsManager
    {
        private static ScriptSettingsManager _instance = new ScriptSettingsManager();

        protected ScriptSettingsManager()
        {
        }

        public static ScriptSettingsManager Instance
        {
            get { return _instance; }
            set { _instance = value; }
        }

        public bool IsAzureEnvironment => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId));

        public bool IsRemoteDebuggingEnabled => !string.IsNullOrEmpty(GetSetting(EnvironmentSettingNames.RemoteDebuggingPort));

        public virtual void Reset()
        {            
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