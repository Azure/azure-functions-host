// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class RunFromPackageContext
    {
        public RunFromPackageContext(string envVarName, string url, long? packageContentLength, bool isWarmupRequest)
        {
            EnvironmentVariableName = envVarName;
            Url = url;
            PackageContentLength = packageContentLength;
            IsWarmUpRequest = isWarmupRequest;
        }

        public string EnvironmentVariableName { get; set; }

        public string Url { get; set; }

        public long? PackageContentLength { get; set; }

        public bool IsWarmUpRequest { get; }

        public bool IsScmRunFromPackage()
        {
            return string.Equals(EnvironmentVariableName, EnvironmentSettingNames.ScmRunFromPackage, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsWebsiteRunFromPackage()
        {
            return string.Equals(EnvironmentVariableName, EnvironmentSettingNames.AzureWebsiteRunFromPackage, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(EnvironmentVariableName, EnvironmentSettingNames.AzureWebsiteAltZipDeployment, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsRunFromPackage(ScriptApplicationHostOptions options, ILogger logger)
        {
            return (IsScmRunFromPackage() && ScmRunFromPackageBlobExists(options, logger)) || (!IsScmRunFromPackage() && !string.IsNullOrEmpty(Url) && Url != "1");
        }

        public bool IsRunFromLocalPackage()
        {
            return IsWebsiteRunFromPackage() && string.Equals(Url, "1", StringComparison.OrdinalIgnoreCase);
        }

        private bool ScmRunFromPackageBlobExists(ScriptApplicationHostOptions options, ILogger logger)
        {
            var blobExists = options.IsScmRunFromPackage;
            logger.LogDebug($"{EnvironmentSettingNames.ScmRunFromPackage} points to an existing blob: {blobExists}");
            return blobExists;
        }
    }
}
