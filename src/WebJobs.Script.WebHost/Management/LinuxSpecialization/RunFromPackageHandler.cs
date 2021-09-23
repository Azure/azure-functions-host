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
                if (pkgContext.IsRunFromLocalPackage())
                {
                    // if local zip (RFP=1), then get full path: zip file name in /home/data/packagename.txt
                    // full path will be like: /home/data/some_file
                    var packageNameFile = "/home/data/packagename.txt";
                    if (!Directory.Exists(packageNameFile))
                    {
                        _logger.LogError($"Application is configured to use WEBSITE_RUN_FROM_PACKAGE=1, but {packageNameFile} does not exist!");
                        return false;
                    }

                    // packagename.txt should consist of one line which contains the name of the package we need to mount
                    var contents = File.ReadLines(packageNameFile);
                    if (contents.Count() != 1)
                    {
                        _logger.LogError($"Application is configured to use WEBSITE_RUN_FROM_PACKAGE=1, but {packageNameFile} is invalid. It should consist only of the package file name.");
                        return false;
                    }

                    var packageName = contents.First();
                    var packagePath = $"/home/data/{packageName}";

                    // mount the package to /home/site/wwwroot
                    _logger.LogInformation($"Mounting {packagePath} to {targetPath}.");
                    await _meshServiceClient.MountFuse(MeshServiceClient.SquashFsOperation, packagePath, targetPath);

                    // initialize file watcher for file that points to package
                    InitializeLocalPackageWatcher(packageNameFile);
                    return true;
                }
                else
                {
                    // If Azure Files are mounted, /home will point to a shared remote dir
                    // So extracting to /home/site/wwwroot can interfere with other running instances
                    // Instead extract to localSitePackagesPath and bind to /home/site/wwwroot
                    // home will continue to point to azure file share

                    var localSitePackagesPath = azureFilesMounted
                        ? _environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.LocalSitePackages,
                            EnvironmentSettingNames.DefaultLocalSitePackagesPath)
                        : string.Empty;

                    // download zip and extract
                    var filePath = await _packageDownloadHandler.Download(pkgContext);
                    await UnpackPackage(filePath, targetPath, pkgContext, localSitePackagesPath);

                    string bundlePath = Path.Combine(targetPath, "worker-bundle");
                    if (Directory.Exists(bundlePath))
                    {
                        _logger.LogInformation($"Python worker bundle detected");
                    }

                    return true;
                }
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

        private void InitializeLocalPackageWatcher(string packageNameFile)
        {
            var filePath = "/home/data/packagename.txt";
            _logger.LogDebug($"Initializing file watcher used for RFP=1. Watching file {filePath}");

            // something like below but figure out how to access event subscriptions/filewatcher service
            // and extract mounting to callable method (outside of specialization context?)
            //_eventSubscriptions.Add(_eventManager.OfType<FileEvent>()
            //            .Where(f => string.Equals(f.Source, packageNameFile, StringComparison.Ordinal))
            //            .Subscribe(e => OnLocalPackageFileChanged(e.FileChangeArguments)));
        }

        private async Task UnpackPackage(string filePath, string scriptPath, RunFromPackageContext pkgContext, string localSitePackagesPath)
        {
            var useLocalSitePackages = !string.IsNullOrEmpty(localSitePackagesPath);
            CodePackageType packageType;
            using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationGetPackageType))
            {
                packageType = GetPackageType(filePath, pkgContext);
            }

            // no need to unsquash, just bind mount to test out (try mountfuse with squashfs)
            if (packageType == CodePackageType.Squashfs)
            {
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
                // default to unzip for zip packages
                if (_environment.IsMountEnabled())
                {
                    await _meshServiceClient.MountFuse(MeshServiceClient.ZipOperation, filePath, scriptPath);
                }
                else
                {
                    if (useLocalSitePackages)
                    {
                        _unZipHandler.UnzipPackage(filePath, localSitePackagesPath);
                        await CreateBindMount(localSitePackagesPath, scriptPath);
                    }
                    else
                    {
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
