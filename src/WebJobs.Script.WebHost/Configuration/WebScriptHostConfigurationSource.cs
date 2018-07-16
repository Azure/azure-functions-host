// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using static System.Environment;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class WebScriptHostConfigurationSource : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new WebScriptHostConfigurationProvider();
        }

        private class WebScriptHostConfigurationProvider : ConfigurationProvider
        {
            private const string ScriptPathProperty = ConfigurationSectionNames.JobHost + ":" + nameof(ScriptHostOptions.RootScriptPath);
            private const string LogPathProperty = ConfigurationSectionNames.JobHost + ":" + nameof(ScriptHostOptions.RootLogPath);
            private const string TestDataPathProperty = ConfigurationSectionNames.JobHost + ":" + nameof(ScriptHostOptions.TestDataPath);
            private const string SecretsPathProperty = ConfigurationSectionNames.WebHost + ":" + nameof(ScriptWebHostOptions.SecretsPath);
            private const string SelfHostProperty = ConfigurationSectionNames.WebHost + ":" + nameof(ScriptWebHostOptions.IsSelfHost);

            public override void Load()
            {
                Data[SelfHostProperty] = (EnvironmentUtility.IsAppServiceEnvironment && !EnvironmentUtility.IsLinuxContainerEnvironment).ToString();

                if (EnvironmentUtility.IsAppServiceEnvironment)
                {
                    // Running in App Service
                    string home = GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
                    Data[ScriptPathProperty] = Path.Combine(home, "site", "wwwroot");
                    Data[LogPathProperty] = Path.Combine(home, "LogFiles", "Application", "Functions");
                    Data[SecretsPathProperty] = Path.Combine(home, "data", "Functions", "secrets");
                    Data[TestDataPathProperty] = Path.Combine(home, "data", "Functions", "sampledata");
                }
                else
                {
                    // Local hosting or Linux container scenarios
                    Data[ScriptPathProperty] = GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsScriptRoot);
                    Data[LogPathProperty] = Path.Combine(Path.GetTempPath(), @"Functions");
                    Data[SecretsPathProperty] = Path.Combine(AppContext.BaseDirectory, "Secrets");
                    Data[TestDataPathProperty] = Path.Combine(Path.GetTempPath(), @"FunctionsData");
                }
            }
        }
    }
}
