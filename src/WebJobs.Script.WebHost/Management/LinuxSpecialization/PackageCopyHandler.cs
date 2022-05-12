// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public class PackageCopyHandler : IPackageCopyHandler
    {
        private readonly ILogger<PackageCopyHandler> _logger;

        public PackageCopyHandler(ILogger<PackageCopyHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string CopyPackageFile()
        {
            var home = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
            var packageFolderPath = Path.Combine(home, "data", "SitePackages");

            if (!Directory.Exists(packageFolderPath))
            {
                _logger.LogWarning($"{nameof(CopyPackageFile)} failed. SitePackages folder in the data folder doesn't exist.");
                return string.Empty;
            }

            var packageNameTxtPath = Path.Combine(packageFolderPath, "packagename.txt");
            if (!File.Exists(packageNameTxtPath))
            {
                _logger.LogWarning($"{nameof(CopyPackageFile)} failed. packagename.txt doesn't exist.");
                return string.Empty;
            }

            var packageFileName = File.ReadAllText(packageNameTxtPath);

            if (string.IsNullOrEmpty(packageFileName))
            {
                _logger.LogWarning($"{nameof(CopyPackageFile)} failed. packagename.txt is empty.");
                return string.Empty;
            }

            var packageFilePath = Path.Combine(packageFolderPath, packageFileName);
            if (!File.Exists(packageFilePath))
            {
                _logger.LogWarning($"{nameof(CopyPackageFile)} failed. {packageFileName} doesn't exist.");
                return string.Empty;
            }

            var packageFileInfo = new FileInfo(packageFilePath);
            if (packageFileInfo.Length == 0)
            {
                _logger.LogWarning($"{nameof(CopyPackageFile)} failed. {packageFileName} size is zero.");
                return string.Empty;
            }

            var tmpPath = Path.GetTempPath();
            var fileName = Path.GetFileName(packageFileName);
            var filePath = Path.Combine(tmpPath, fileName);

            File.Copy(packageFilePath, filePath, true);

            _logger.LogInformation($"{nameof(CopyPackageFile)} was successfull. {packageFileName} was copied to {filePath}.");

            return filePath;
        }
    }
}
