// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Management;
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
            return string.Equals(EnvironmentVariableName, EnvironmentSettingNames.ScmRunFromPackage,
                        StringComparison.OrdinalIgnoreCase);
        }

        public bool IsRunFromPackage(ScriptApplicationHostOptions options)
        {
            return (IsScmRunFromPackage() && options.ScmRunFromPackageBlobExists) || (!IsScmRunFromPackage() && !string.IsNullOrEmpty(Url) && Url != "1");
        }
    }
}
