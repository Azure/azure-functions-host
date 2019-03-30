// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using static System.Environment;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class WebScriptHostConfigurationSource : IConfigurationSource
    {
        public bool IsAppServiceEnvironment { get; set; }

        public bool IsLinuxContainerEnvironment { get; set; }

        public bool IsLinuxAppServiceEnvironment { get; set; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new WebScriptHostConfigurationProvider(this);
        }

        private class WebScriptHostConfigurationProvider : ConfigurationProvider
        {
            private const string KeyDelimiter = ":";
            private const string LogPathProperty = ConfigurationSectionNames.WebHost + KeyDelimiter + nameof(ScriptApplicationHostOptions.LogPath);
            private const string TestDataPathProperty = ConfigurationSectionNames.WebHost + KeyDelimiter + nameof(ScriptApplicationHostOptions.TestDataPath);
            private const string SecretsPathProperty = ConfigurationSectionNames.WebHost + KeyDelimiter + nameof(ScriptApplicationHostOptions.SecretsPath);
            private const string SelfHostProperty = ConfigurationSectionNames.WebHost + KeyDelimiter + nameof(ScriptApplicationHostOptions.IsSelfHost);
            private const string WebHostScriptPathProperty = ConfigurationSectionNames.WebHost + KeyDelimiter + nameof(ScriptApplicationHostOptions.ScriptPath);

            private readonly WebScriptHostConfigurationSource _configurationSource;

            public WebScriptHostConfigurationProvider(WebScriptHostConfigurationSource configurationSource)
            {
                _configurationSource = configurationSource ?? throw new ArgumentNullException(nameof(configurationSource));
            }

            public override void Load()
            {
                Data[SelfHostProperty] = (!_configurationSource.IsAppServiceEnvironment && !_configurationSource.IsLinuxContainerEnvironment).ToString();

                if (_configurationSource.IsAppServiceEnvironment)
                {
                    // Running in App Service
                    string home = GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
                    Data[WebHostScriptPathProperty] = Path.Combine(home, "site", "wwwroot");
                    Data[LogPathProperty] = Path.Combine(home, "LogFiles", "Application", "Functions");
                    Data[SecretsPathProperty] = Path.Combine(home, "data", "Functions", "secrets");
                    Data[TestDataPathProperty] = Path.Combine(home, "data", "Functions", "sampledata");
                }
                else
                {
                    // Local hosting or Linux container scenarios
                    Data[WebHostScriptPathProperty] = GetOrDefault(EnvironmentSettingNames.AzureWebJobsScriptRoot, Environment.CurrentDirectory);
                    Data[LogPathProperty] = GetOrDefault(EnvironmentSettingNames.FunctionsLogPath, Path.Combine(Path.GetTempPath(), @"Functions"));
                    Data[SecretsPathProperty] = GetOrDefault(EnvironmentSettingNames.FunctionsSecretsPath, Path.Combine(AppContext.BaseDirectory, "Secrets"));
                    Data[TestDataPathProperty] = GetOrDefault(EnvironmentSettingNames.FunctionsTestDataPath, Path.Combine(Path.GetTempPath(), @"FunctionsData"));
                }

                string GetOrDefault(string variableName, string @default)
                {
                    var result = GetEnvironmentVariable(variableName);
                    return string.IsNullOrEmpty(result) ? @default : result;
                }
            }
        }
    }
}
