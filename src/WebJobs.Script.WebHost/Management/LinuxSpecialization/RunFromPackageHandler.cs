// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization
{
    public class RunFromPackageHandler : IRunFromPackageHandler
    {
        public const int AriaDownloadThreshold = 100 * 1024 * 1024;
        public const string Aria2CExecutable = "aria2c";
        public const string UnsquashFSExecutable = "unsquashfs";
        private const string SquashfsPrefix = "Squashfs";
        private const string ZipPrefix = "Zip";
        private static readonly string[] _knownPackageExtensions = { ".squashfs", ".sfs", ".sqsh", ".img", ".fs" };

        private readonly IEnvironment _environment;
        private readonly HttpClient _client;
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly IBashCommandHandler _bashCommandHandler;
        private readonly IUnZipHandler _unZipHandler;
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILogger<RunFromPackageHandler> _logger;

        public RunFromPackageHandler(IEnvironment environment, HttpClient client, IMeshServiceClient meshServiceClient,
            IBashCommandHandler bashCommandHandler, IUnZipHandler unZipHandler, IMetricsLogger metricsLogger, ILogger<RunFromPackageHandler> logger)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _meshServiceClient = meshServiceClient ?? throw new ArgumentNullException(nameof(meshServiceClient));
            _bashCommandHandler = bashCommandHandler ?? throw new ArgumentNullException(nameof(bashCommandHandler));
            _unZipHandler = unZipHandler;
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

                // download zip and extract
                var filePath = await Download(pkgContext);
                await UnpackPackage(filePath, targetPath, pkgContext, localSitePackagesPath);

                string bundlePath = Path.Combine(targetPath, "worker-bundle");
                if (Directory.Exists(bundlePath))
                {
                    _logger.LogInformation($"Python worker bundle detected");
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

        private async Task UnpackPackage(string filePath, string scriptPath, RunFromPackageContext pkgContext, string localSitePackagesPath)
        {
            var useLocalSitePackages = !string.IsNullOrEmpty(localSitePackagesPath);
            CodePackageType packageType;
            using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationGetPackageType))
            {
                packageType = GetPackageType(filePath, pkgContext);
            }

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

        public async Task<string> Download(RunFromPackageContext pkgContext)
        {
            var zipUri = new Uri(pkgContext.Url);
            if (!Utility.TryCleanUrl(zipUri.AbsoluteUri, out string cleanedUrl))
            {
                throw new InvalidOperationException("Invalid url for the package");
            }

            var tmpPath = Path.GetTempPath();
            var fileName = Path.GetFileName(zipUri.AbsolutePath);
            var filePath = Path.Combine(tmpPath, fileName);
            if (pkgContext.PackageContentLength != null && pkgContext.PackageContentLength > AriaDownloadThreshold)
            {
                _logger.LogDebug($"Downloading zip contents from '{cleanedUrl}' using aria2c'");
                AriaDownload(tmpPath, fileName, zipUri, pkgContext.IsWarmUpRequest);
            }
            else
            {
                _logger.LogDebug($"Downloading zip contents from '{cleanedUrl}' using httpclient'");
                await HttpClientDownload(filePath, zipUri, pkgContext.IsWarmUpRequest);
            }

            return filePath;
        }

        private void AriaDownload(string directory, string fileName, Uri zipUri, bool isWarmupRequest)
        {
            var metricName = isWarmupRequest
                ? MetricEventNames.LinuxContainerSpecializationZipDownloadWarmup
                : MetricEventNames.LinuxContainerSpecializationZipDownload;
            (string stdout, string stderr, int exitCode) = _bashCommandHandler.RunBashCommand(
                $"{Aria2CExecutable} --allow-overwrite -x12 -d {directory} -o {fileName} '{zipUri}'",
                metricName);
            if (exitCode != 0)
            {
                var msg = $"Error downloading package. stdout: {stdout}, stderr: {stderr}, exitCode: {exitCode}";
                _logger.LogError(msg);
                throw new InvalidOperationException(msg);
            }
            var fileInfo = FileUtility.FileInfoFromFileName(Path.Combine(directory, fileName));
            _logger.LogInformation($"{fileInfo.Length} bytes downloaded. IsWarmupRequest = {isWarmupRequest}");
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

        private async Task HttpClientDownload(string filePath, Uri zipUri, bool isWarmupRequest)
        {
            HttpResponseMessage response = null;
            await Utility.InvokeWithRetriesAsync(async () =>
            {
                try
                {
                    var downloadMetricName = isWarmupRequest
                        ? MetricEventNames.LinuxContainerSpecializationZipDownloadWarmup
                        : MetricEventNames.LinuxContainerSpecializationZipDownload;
                    using (_metricsLogger.LatencyEvent(downloadMetricName))
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, zipUri);
                        response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                    }
                }
                catch (Exception e)
                {
                    string error = $"Error downloading zip content";
                    _logger.LogError(e, error);
                    throw;
                }
                _logger.LogInformation($"{response.Content.Headers.ContentLength} bytes downloaded. IsWarmupRequest = {isWarmupRequest}");
            }, 2, TimeSpan.FromSeconds(0.5));

            using (_metricsLogger.LatencyEvent(isWarmupRequest ? MetricEventNames.LinuxContainerSpecializationZipWriteWarmup : MetricEventNames.LinuxContainerSpecializationZipWrite))
            {
                using (var content = await response.Content.ReadAsStreamAsync())
                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                {
                    await content.CopyToAsync(stream);
                }
                _logger.LogInformation($"{response.Content.Headers.ContentLength} bytes written. IsWarmupRequest = {isWarmupRequest}");
            }
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
