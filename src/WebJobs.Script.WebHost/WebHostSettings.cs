// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebHostSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether the host is running
        /// outside of the normal Azure hosting environment. E.g. when running
        /// locally or via CLI.
        /// </summary>
        public bool IsSelfHost { get; set; }

        public string ScriptPath { get; set; }

        public string LogPath { get; set; }

        public string SecretsPath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether authentication/authorization
        /// should be disabled. Useful for local debugging or CLI scenarios.
        /// </summary>
        public bool IsAuthDisabled { get; set; } = false;

        public TraceWriter TraceWriter { get; set; }

        public ILoggerFactoryBuilder LoggerFactoryBuilder { get; set; } = new DefaultLoggerFactoryBuilder();

        internal static WebHostSettings CreateDefault(ScriptSettingsManager settingsManager)
        {
            WebHostSettings settings = new WebHostSettings
            {
                IsSelfHost = !settingsManager.IsAzureEnvironment
            };

            if (settingsManager.IsAzureEnvironment)
            {
                string home = settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
                settings.ScriptPath = Path.Combine(home, @"site\wwwroot");
                settings.LogPath = Path.Combine(home, @"LogFiles\Application\Functions");
                settings.SecretsPath = Path.Combine(home, @"data\Functions\secrets");
            }
            else
            {
                settings.ScriptPath = settingsManager.GetSetting(EnvironmentSettingNames.AzureWebJobsScriptRoot);
                settings.LogPath = Path.Combine(Path.GetTempPath(), @"Functions");

                // TODO: Revisit. We'll likely have to take an instance of an IHostingEnvironment here
                settings.SecretsPath = Path.Combine(AppContext.BaseDirectory, "Secrets");
            }

            if (string.IsNullOrEmpty(settings.ScriptPath))
            {
                throw new InvalidOperationException("Unable to determine function script root directory.");
            }

            return settings;
        }
    }
}