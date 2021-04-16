// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FileSystemManager : IFileSystemManager
    {
        private static bool? _blobExists = null;
        private readonly IEnvironment _environment;
        private readonly CloudBlockBlobHelperService _cloudBlockBlobHelperService;

        public FileSystemManager(IEnvironment environment, CloudBlockBlobHelperService cloudBlockBlobHelperService = null)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _cloudBlockBlobHelperService = cloudBlockBlobHelperService ?? new CloudBlockBlobHelperService();
        }

        public bool IsFileSystemReadOnly(ILogger logger)
        {
            return IsZipDeployment(logger);
        }

        public bool IsZipDeployment(ILogger logger, bool validate = true)
        {
            if (validate)
            {
                // If the app is using the new app settings for zip deployment, we don't need to check further, it must be a zip deployment.
                bool usesNewZipDeployAppSettings = IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteZipDeployment)) ||
                    IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteAltZipDeployment)) ||
                    IsValidZipSetting(_environment.GetEnvironmentVariable(AzureWebsiteRunFromPackage));

                if (usesNewZipDeployAppSettings)
                {
                    return usesNewZipDeployAppSettings;
                }

                // Check if the old app setting for zip deployment is set, SCM_RUN_FROM_PACKAGE.
                // If not, this is not a zip deployment. If yes, we still need to check if the blob exists,
                // since this app setting is always created for Linux Consumption, even when it is not used.
                // In portal deployment, SCM_RUN_FROM_PACKAGE will be set, but we will be using Azure Files instead.
                if (!IsValidZipUrl(_environment.GetEnvironmentVariable(ScmRunFromPackage)))
                {
                    return false;
                }

                // If SCM_RUN_FROM_PACKAGE is a valid app setting and Azure Files are not being used, it must be a zip deployment.
                if (string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureFilesConnectionString)) &&
                    string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureFilesContentShare)))
                {
                    return true;
                }

                // SCM_RUN_FROM_PACKAGE is set, as well as Azure Files app settings, so we need to check if we are actually using the zip blob.
                if (_blobExists == null)
                {
                    CacheIfBlobExists(logger);
                }

                return _blobExists.Value;
            }
            else
            {
                return !string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureWebsiteZipDeployment)) ||
                    !string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureWebsiteAltZipDeployment)) ||
                    !string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureWebsiteRunFromPackage)) ||
                    !string.IsNullOrEmpty(_environment.GetEnvironmentVariable(ScmRunFromPackage));
            }
        }

        public void CacheIfBlobExists(ILogger logger)
        {
            if (_blobExists != null)
            {
                // The result is already cached
                return;
            }

            if (string.IsNullOrEmpty(_environment.GetEnvironmentVariable(ScmRunFromPackage)))
            {
                _blobExists = false;
            }
            else
            {
                if (string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureFilesConnectionString)) &&
                    string.IsNullOrEmpty(_environment.GetEnvironmentVariable(AzureFilesContentShare)))
                {
                    _blobExists = true;
                }
                else
                {
                    // Check if blob exists only if both SCM_RUN_FROM_PACKAGE and Azure Files app settings are present.
                    _blobExists = _cloudBlockBlobHelperService.BlobExists(_environment.GetEnvironmentVariable(ScmRunFromPackage), EnvironmentSettingNames.ScmRunFromPackage, logger).GetAwaiter().GetResult();
                    logger.LogInformation($"Cached if ${EnvironmentSettingNames.ScmRunFromPackage} points to an existing blob: ${_blobExists.Value}");
                }
            }
        }

        private static bool IsValidZipSetting(string appSetting)
        {
            // valid values are 1 or an absolute URI
            return string.Equals(appSetting, "1") || IsValidZipUrl(appSetting);
        }

        private static bool IsValidZipUrl(string appSetting)
        {
            return Uri.TryCreate(appSetting, UriKind.Absolute, out Uri result);
        }
    }
}
