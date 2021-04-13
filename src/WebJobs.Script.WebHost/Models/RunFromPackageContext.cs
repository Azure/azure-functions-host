// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class RunFromPackageContext
    {
        private readonly RunFromPackageCloudBlockBlobService _runFromPackageCloudBlockBlobService;

        public RunFromPackageContext(string envVarName, string url, long? packageContentLength, bool isWarmupRequest, RunFromPackageCloudBlockBlobService runFromPackageCloudBlockBlobService = null)
        {
            _runFromPackageCloudBlockBlobService = runFromPackageCloudBlockBlobService ?? new RunFromPackageCloudBlockBlobService();
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

        public async Task<bool> IsRunFromPackage(ILogger logger)
        {
            return (IsScmRunFromPackage() && await _runFromPackageCloudBlockBlobService.BlobExists(Url, EnvironmentVariableName, logger)) ||
                   (!IsScmRunFromPackage() && !string.IsNullOrEmpty(Url) && Url != "1");
        }
    }
}
