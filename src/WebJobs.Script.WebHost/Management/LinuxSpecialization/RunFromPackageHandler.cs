// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public class RunFromPackageHandler : IRunFromPackageHandler
    {
        public const string UnsquashFSExecutable = "unsquashfs";
        private const string SquashfsPrefix = "Squashfs";
        private const string ZipPrefix = "Zip";
        private static readonly string[] _knownPackageExtensions = { ".squashfs", ".sfs", ".sqsh", ".img", ".fs" };

        private readonly IEnvironment _environment;
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly IBashCommandHandler _bashCommandHandler;
        private readonly IUnZipHandler _unZipHandler;
        private readonly IPackageDownloadHandler _packageDownloadHandler;
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger<RunFromPackageHandler> _logger;

        public RunFromPackageHandler(IEnvironment environment, IMeshServiceClient meshServiceClient,
            IBashCommandHandler bashCommandHandler, IUnZipHandler unZipHandler, IPackageDownloadHandler packageDownloadHandler, IMetricsLogger metricsLogger, ILogger<RunFromPackageHandler> logger)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _meshServiceClient = meshServiceClient ?? throw new ArgumentNullException(nameof(meshServiceClient));
            _bashCommandHandler = bashCommandHandler ?? throw new ArgumentNullException(nameof(bashCommandHandler));
            _unZipHandler = unZipHandler ?? throw new ArgumentNullException(nameof(unZipHandler));
            _packageDownloadHandler = packageDownloadHandler ?? throw new ArgumentNullException(nameof(packageDownloadHandler));
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> ApplyBlobPackageContext(RunFromPackageContext pkgContext, string targetPath, bool azureFilesMounted, bool throwOnFailure = true)
        {
            try
            {
                // If Azure Files are mounted, /home will point to a shared remote dir
                // So extracting to /home/site/wwwroot can interfere with other running instances
                // Instead extract to localSitePackagesPath and bind to /home/site/wwwroot
                // home will continue to point to azure file share
                var localSitePackagesPath = azureFilesMounted
                    ? _environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.LocalSitePackages,
                        EnvironmentSettingNames.DefaultLocalSitePackagesPath)
                    : string.Empty;

                // khkh: temp condition.
                _logger.LogInformation($"azureFilesMounted Status: {azureFilesMounted}");
                if (azureFilesMounted)
                {
                    var thepath = _environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.LocalSitePackages,
                        EnvironmentSettingNames.DefaultLocalSitePackagesPath);
                    _logger.LogInformation($"LocalSitePackes Path: {thepath}");

                    var home = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
                    _logger.LogInformation($"Home Path: {home}");

                    var potentialPackageFolderPath = Path.Combine(home, "data", "SitePackages");
                    _logger.LogInformation($"Potential PackageFolderPath: {potentialPackageFolderPath}");
                }

                string filePath;

                if (!pkgContext.IsRunFromLocalPackage())
                {
                    _logger.LogInformation($"{nameof(ApplyBlobPackageContext)}: Going to download package file.");
                    // download zip
                    filePath = await _packageDownloadHandler.Download(pkgContext);
                }
                else
                {
                    _logger.LogInformation($"{nameof(ApplyBlobPackageContext)}: Going to copy package file.");
                    if (!azureFilesMounted)
                    {
                        _logger.LogWarning($"{nameof(ApplyBlobPackageContext)} failed. FileShareMount is required when RunFromPackage is 1.");
                    }

                    filePath = CopyPackageFile();

                    if (string.IsNullOrEmpty(filePath))
                    {
                        return false;
                    }
                }

                // extract zip
                await UnpackPackage(filePath, targetPath, pkgContext, localSitePackagesPath);

                string bundlePath = Path.Combine(targetPath, "worker-bundle");
                if (Directory.Exists(bundlePath))
                {
                    _logger.LogInformation("Python worker bundle detected");
                }

                return true;
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, nameof(ApplyBlobPackageContext));
                if (throwOnFailure)
                {
                    throw;
                }

                return false;
            }
        }

        private string CopyPackageFile()
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

        private async Task UnpackPackage(string filePath, string scriptPath, RunFromPackageContext pkgContext, string localSitePackagesPath)
        {
            _logger.LogInformation($"{nameof(UnpackPackage)}: filepath {filePath}, script path {scriptPath}, localSitePackagePath {localSitePackagesPath}.");

            var useLocalSitePackages = !string.IsNullOrEmpty(localSitePackagesPath);
            CodePackageType packageType;

            _logger.LogInformation($"{nameof(UnpackPackage)}: Going to get packagetype.");
            packageType = GetPackageType(filePath, pkgContext);
            _logger.LogInformation($"{nameof(UnpackPackage)}: packagetype. {packageType}.");
            //using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationGetPackageType))
            //{
            //    _logger.LogInformation($"{nameof(UnpackPackage)}: Going to get packagetype.");
            //    packageType = GetPackageType(filePath, pkgContext);
            //    _logger.LogInformation($"{nameof(UnpackPackage)}: packagetype. {packageType}.");
            //}

            if (packageType == CodePackageType.Squashfs)
            {
                _logger.LogInformation($"{nameof(UnpackPackage)}: packagetype is Squashfs.");
                // default to mount for squashfs images
                if (_environment.IsMountDisabled())
                {
                    if (useLocalSitePackages)
                    {
                        UnsquashImage(filePath, localSitePackagesPath);
                        await CreateBindMount(localSitePackagesPath, scriptPath);
                    }
                    else
                    {
                        UnsquashImage(filePath, scriptPath);
                    }
                }
                else
                {
                    await _meshServiceClient.MountFuse(MeshServiceClient.SquashFsOperation, filePath, scriptPath);
                }
            }
            else if (packageType == CodePackageType.Zip)
            {
                _logger.LogInformation($"{nameof(UnpackPackage)}: packagetype is zip.");
                // default to unzip for zip packages
                if (_environment.IsMountEnabled())
                {
                    _logger.LogInformation($"{nameof(UnpackPackage)}: Calling MountFuse on zip.");
                    await _meshServiceClient.MountFuse(MeshServiceClient.ZipOperation, filePath, scriptPath);
                    _logger.LogInformation($"{nameof(UnpackPackage)}: MountFuse completed on zip ");
                }
                else
                {
                    if (useLocalSitePackages)
                    {
                        _logger.LogInformation($"{nameof(UnpackPackage)}: Unziping {filePath} to localSitepackage at {localSitePackagesPath}");
                        _unZipHandler.UnzipPackage(filePath, localSitePackagesPath);
                        _logger.LogInformation($"{nameof(UnpackPackage)}: Calling CreateBindMount on zip.");
                        await CreateBindMount(localSitePackagesPath, scriptPath);
                        _logger.LogInformation($"{nameof(UnpackPackage)}: CreateBindMount completed on zip ");
                    }
                    else
                    {
                        _logger.LogInformation($"{nameof(UnpackPackage)}: Unziping {filePath}");
                        _unZipHandler.UnzipPackage(filePath, scriptPath);
                    }
                }
            }
        }

        private CodePackageType GetPackageType(string filePath, RunFromPackageContext pkgContext)
        {
            // cloud build always builds squashfs
            if (pkgContext.IsScmRunFromPackage())
            {
                return CodePackageType.Squashfs;
            }

            var uri = new Uri(pkgContext.Url);
            // check file name since it'll be faster than running `file`
            if (FileIsAny(_knownPackageExtensions))
            {
                return CodePackageType.Squashfs;
            }
            else if (FileIsAny(".zip"))
            {
                return CodePackageType.Zip;
            }

            // Check file magic-number using `file` command.
            (var output, _, _) = _bashCommandHandler.RunBashCommand($"{BashCommandHandler.FileCommand} -b {filePath}", MetricEventNames.LinuxContainerSpecializationFileCommand);
            if (output.StartsWith(SquashfsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return CodePackageType.Squashfs;
            }
            else if (output.StartsWith(ZipPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return CodePackageType.Zip;
            }
            else
            {
                throw new InvalidOperationException($"Can't find CodePackageType to match {filePath}");
            }

            bool FileIsAny(params string[] options)
                => options.Any(o => uri.AbsolutePath.EndsWith(o, StringComparison.OrdinalIgnoreCase));
        }

        private async Task CreateBindMount(string sourcePath, string targetPath)
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationBindMount))
            {
                await _meshServiceClient.CreateBindMount(sourcePath, targetPath);
            }
        }

        private void UnsquashImage(string filePath, string scriptPath)
        {
            _logger.LogDebug($"Unsquashing remote zip to {scriptPath}");

            _bashCommandHandler.RunBashCommand($"{UnsquashFSExecutable} -f -d '{scriptPath}' '{filePath}'",
                MetricEventNames.LinuxContainerSpecializationUnsquash);
        }

        public async Task<bool> MountAzureFileShare(HostAssignmentContext assignmentContext)
        {
            try
            {
                using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationMountCifs))
                {
                    var targetPath = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
                    _logger.LogDebug($"Mounting {EnvironmentSettingNames.AzureFilesContentShare} at {targetPath}");
                    bool succeeded = await _meshServiceClient.MountCifs(assignmentContext.AzureFilesConnectionString,
                        assignmentContext.AzureFilesContentShare, targetPath);
                    _logger.LogInformation($"Mounted {EnvironmentSettingNames.AzureFilesContentShare} at {targetPath} Success = {succeeded}");
                    return succeeded;
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, nameof(MountAzureFileShare));
                return false;
            }
        }
    }
}
