// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class WebHostSettingsExtensions
    {
        public static ScriptJobHostOptions ToHostOptions(this ScriptApplicationHostOptions settings, bool inStandbyMode = false)
        {
            var scriptHostConfig = new ScriptJobHostOptions()
            {
                RootScriptPath = settings.ScriptPath,
                RootLogPath = settings.LogPath,
                FileLoggingMode = FileLoggingMode.DebugOnly,
                IsSelfHost = settings.IsSelfHost,
                TestDataPath = settings.TestDataPath
            };

            if (inStandbyMode)
            {
                scriptHostConfig.FileLoggingMode = FileLoggingMode.DebugOnly;
                // TODO: DI (FACAVAL) This should no longer be needed... handled at initialization
                //scriptHostConfig.HostConfig.StorageConnectionString = null;
                //scriptHostConfig.HostConfig.DashboardConnectionString = null;
            }

            return scriptHostConfig;
        }
    }
}