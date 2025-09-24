// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle
{
    public class ExtensionBundleManager : IExtensionBundleManager
    {
        private const string ExtensionBundleClientName = nameof(ExtensionBundleManager);
        private readonly IEnvironment _environment;
        private readonly ExtensionBundleOptions _options;
        private readonly FunctionsHostingConfigOptions _configOption;
        private readonly ILogger _logger;
        private readonly string _cdnUri;
        private readonly string _platformReleaseChannel;
        private readonly IHttpClientFactory _httpClientFactory;
        private string _extensionBundleVersion;

        public ExtensionBundleManager(ExtensionBundleOptions options, IEnvironment environment, ILoggerFactory loggerFactory, FunctionsHostingConfigOptions configOption, IHttpClientFactory httpClientFactory)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = loggerFactory.CreateLogger<ExtensionBundleManager>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _configOption = configOption ?? throw new ArgumentNullException(nameof(configOption));
            _cdnUri = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ExtensionBundleSourceUri) ?? ScriptConstants.ExtensionBundleDefaultSourceUri;
            _platformReleaseChannel = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformReleaseChannel) ?? ScriptConstants.LatestPlatformChannelNameUpper;
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public async Task<ExtensionBundleDetails> GetExtensionBundleDetails()
        {
            if (IsExtensionBundleConfigured())
            {
                if (_extensionBundleVersion == null && TryLocateExtensionBundle(out string path))
                {
                    _extensionBundleVersion = Path.GetFileName(path);
                }

                _extensionBundleVersion = _extensionBundleVersion ?? await GetLatestMatchingBundleVersionAsync();

                return new ExtensionBundleDetails()
                {
                    Id = _options.Id,
                    Version = _extensionBundleVersion
                };
            }

            return null;
        }

        public bool IsExtensionBundleConfigured()
        {
            return !string.IsNullOrEmpty(_options.Id) && !string.IsNullOrEmpty(_options.Version?.OriginalString);
        }

        public bool IsLegacyExtensionBundle()
        {
            return IsExtensionBundleConfigured()
                && _options.Id == ScriptConstants.DefaultExtensionBundleId
                && (_options.Version.MaxVersion <= ScriptConstants.ExtensionBundleVersionTwo && !_options.Version.IsMaxInclusive);
        }

        /// <summary>
        /// Attempts to locate the extension bundle inside the probing paths and download paths. If the extension bundle is not found then it will download the extension bundle.
        /// </summary>
        /// <returns>Path of the extension bundle.</returns>
        public async Task<string> GetExtensionBundlePath()
        {
            var client = _httpClientFactory.CreateClient(ExtensionBundleClientName);
            return await GetBundle(client);
        }

        /// <summary>
        /// Attempts to locate the extension bundle inside the probing paths and download paths. If the extension bundle is not found then it will download the extension bundle.
        /// </summary>
        /// <param name="httpClient">HttpClient used to download the extension bundle.</param>
        /// <returns>Path of the extension bundle.</returns>
        public async Task<string> GetExtensionBundlePath(HttpClient httpClient)
        {
            return await GetBundle(httpClient);
        }

        private async Task<string> GetBundle(HttpClient httpClient)
        {
            bool bundleFound = TryLocateExtensionBundle(out string bundlePath);

            if ((_environment.IsAppService()
                || _environment.IsCoreTools()
                || _environment.IsAnyLinuxConsumption()
                || _environment.IsContainer())
                && (!bundleFound || _options.EnsureLatest))
            {
                string latestBundleVersion = await GetLatestMatchingBundleVersionAsync(httpClient);
                if (string.IsNullOrEmpty(latestBundleVersion))
                {
                    return null;
                }

                _extensionBundleVersion = latestBundleVersion;
                bundlePath = await DownloadExtensionBundleAsync(latestBundleVersion, httpClient);
            }
            return bundlePath;
        }

        internal bool TryLocateExtensionBundle(out string bundlePath)
        {
            bundlePath = null;
            string bundleMetatdataFile = null;
            var paths = new List<string>(_options.ProbingPaths)
                {
                    _options.DownloadPath
                };

            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                _logger.LocateExtensionBundle(_options.Id, path);
                if (FileUtility.DirectoryExists(path))
                {
                    var bundleDirectories = FileUtility.EnumerateDirectories(path);
                    string version = FindBestVersionMatch(_options.Version, bundleDirectories, _options.Id, _configOption);

                    if (!string.IsNullOrEmpty(version))
                    {
                        bundlePath = Path.Combine(path, version);
                        bundleMetatdataFile = Path.Combine(bundlePath, ScriptConstants.ExtensionBundleMetadataFile);
                        if (!string.IsNullOrEmpty(bundleMetatdataFile) && FileUtility.FileExists(bundleMetatdataFile))
                        {
                            _logger.ExtensionBundleFound(bundlePath);
                            break;
                        }
                        else
                        {
                            bundlePath = null;
                        }
                    }
                }
            }
            return bundlePath != null;
        }

        private async Task<string> DownloadExtensionBundleAsync(string version, HttpClient httpClient)
        {
            string bundleMetatdataFile = Path.Combine(_options.DownloadPath, version, ScriptConstants.ExtensionBundleMetadataFile);
            string bundlePath = Path.Combine(_options.DownloadPath, version);
            if (FileUtility.FileExists(bundleMetatdataFile))
            {
                _logger.LogInformation($"Skipping bundle download since it already exists at path {bundlePath}");
                return bundlePath;
            }

            string zipDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            FileUtility.EnsureDirectoryExists(zipDirectoryPath);

            string zipFilePath = Path.Combine(zipDirectoryPath, $"{_options.Id}.{version}.zip");

            // construct a string based on os type
            string bundleFlavor = GetBundleFlavorForCurrentEnvironment();
            var zipUri = new Uri($"{_cdnUri}/{ScriptConstants.ExtensionBundleDirectory}/{_options.Id}/{version}/{_options.Id}.{version}_{bundleFlavor}.zip");

            if (await TryDownloadZipFileAsync(zipUri, zipFilePath, httpClient))
            {
                FileUtility.EnsureDirectoryExists(bundlePath);

                _logger.ExtractingBundleZip(bundlePath);
                ZipFile.ExtractToDirectory(zipFilePath, bundlePath);
                _logger.ZipExtractionComplete();
            }
            return FileUtility.FileExists(bundleMetatdataFile) ? bundlePath : null;
        }

        private string GetBundleFlavorForCurrentEnvironment()
        {
            if (_environment.IsWindowsAzureManagedHosting())
            {
                return ScriptConstants.ExtensionBundleForAppServiceWindows;
            }

            if (_environment.IsLinuxAzureManagedHosting())
            {
                return ScriptConstants.ExtensionBundleForAppServiceLinux;
            }

            return ScriptConstants.ExtensionBundleForNonAppServiceEnvironment;
        }

        private async Task<bool> TryDownloadZipFileAsync(Uri zipUri, string filePath, HttpClient httpClient, CancellationToken cancellationToken = default)
        {
            string azureRef = string.Empty;
            try
            {
                _logger.DownloadingZip(zipUri, filePath);

                using var response = await httpClient.GetAsync(zipUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                // Log AzureRef header if present (debug level to avoid noise in normal operations)
                response.TryGetAzureRef(out azureRef);

                response.EnsureSuccessStatusCode();

                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
                await response.Content.CopyToAsync(fileStream, cancellationToken);
                await fileStream.FlushAsync(cancellationToken);

                _logger.DownloadComplete(zipUri, filePath);

                return true;
            }
            catch (HttpRequestException ex)
            {
                var statusCode = ex.StatusCode;
                _logger.ErrorDownloadingExtensionBundleZipHttpRequest(
                    ex,
                    zipUri,
                    statusCode,
                    ex.HttpRequestError,
                    filePath,
                    GetDiskUsageSafe(filePath),
                    azureRef);
                return false;
            }
            catch (IOException ex)
            {
                _logger.ErrorDownloadingExtensionBundleZipIO(
                    ex,
                    zipUri,
                    filePath,
                    GetDiskUsageSafe(filePath),
                    azureRef);
                return false;
            }
            catch (Exception ex)
            {
                // Non-HttpRequestException path: log as unexpected without HTTP-specific fields.
                _logger.ErrorDownloadingExtensionBundleZipUnexpected(
                    ex,
                    zipUri,
                    filePath,
                    GetDiskUsageSafe(filePath),
                    azureRef);

                return false;
            }
        }

        private string GetDiskUsageSafe(string path)
        {
            try
            {
                var root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root))
                {
                    return "error=RootPathNotFound";
                }

                var di = new DriveInfo(root);
                const double BytesPerMB = 1024d * 1024d;
                double freeMb = di.AvailableFreeSpace / BytesPerMB;
                double totalMb = di.TotalSize / BytesPerMB;
                return $"free={freeMb:F2}MB total={totalMb:F2}MB";
            }
            catch (Exception ex)
            {
                return FormatDiskError(ex);
            }
        }

        private static string FormatDiskError(Exception ex)
        {
            var msg = ex.Message?.Replace(Environment.NewLine, " ").Trim();
            if (!string.IsNullOrEmpty(msg) && msg.Length > 200)
            {
                msg = msg.Substring(0, 200) + "...";
            }
            return $"error={ex.GetType().Name}: {msg}";
        }

        private async Task<string> GetLatestMatchingBundleVersionAsync()
        {
            var client = _httpClientFactory.CreateClient(ExtensionBundleClientName);
            return await GetLatestMatchingBundleVersionAsync(client);
        }

        private async Task<string> GetLatestMatchingBundleVersionAsync(HttpClient httpClient)
        {
            var uri = new Uri($"{_cdnUri}/{ScriptConstants.ExtensionBundleDirectory}/{_options.Id}/{ScriptConstants.ExtensionBundleVersionIndexFile}");
            _logger.FetchingVersionInfo(_options.Id, uri);

            var response = await httpClient.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                _logger.ErrorFetchingVersionInfo(_options.Id);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var bundleVersions = JsonConvert.DeserializeObject<IEnumerable<string>>(content);

            var matchingBundleVersion = FindBestVersionMatch(_options.Version, bundleVersions, _options.Id, _configOption);

            if (string.IsNullOrEmpty(matchingBundleVersion))
            {
                _logger.MatchingBundleNotFound(_options.Version.OriginalString);
            }

            return matchingBundleVersion;
        }

        internal string FindBestVersionMatch(VersionRange versionRange, IEnumerable<string> versions, string bundleId, FunctionsHostingConfigOptions configOption)
        {
            var bundleVersions = versions.Select(p =>
            {
                var dirName = Path.GetFileName(p);
                NuGetVersion.TryParse(dirName, out NuGetVersion version);
                if (version != null)
                {
                    version = versionRange.Satisfies(version) ? version : null;
                }
                return version;
            }).Where(v => v != null).OrderByDescending(version => version.Version).ToList();

            var matchingVersion = ResolvePlatformReleaseChannelVersion(bundleVersions);

            if (bundleId != ScriptConstants.DefaultExtensionBundleId)
            {
                return matchingVersion?.ToString();
            }

            // Check to see if there is a max bundle version set via hosting configuration, if yes then use that instead of the one
            // available on VM or local machine. Only use MaximumBundleV3Version or MaximumBundleV4Version if the version configured
            // by the customer resolved to version higher than the version set via hosting config.
            if (!string.IsNullOrEmpty(configOption.MaximumBundleV3Version)
                && matchingVersion?.Major == ScriptConstants.ExtensionBundleV3MajorVersion)
            {
                var maximumBundleV3Version = NuGetVersion.Parse(configOption.MaximumBundleV3Version);
                matchingVersion = matchingVersion > maximumBundleV3Version ? maximumBundleV3Version : matchingVersion;
                return matchingVersion?.ToString();
            }

            if (!string.IsNullOrEmpty(configOption.MaximumBundleV4Version)
                && matchingVersion?.Major == ScriptConstants.ExtensionBundleV4MajorVersion)
            {
                var maximumBundleV4Version = NuGetVersion.Parse(configOption.MaximumBundleV4Version);
                matchingVersion = matchingVersion > maximumBundleV4Version
                                ? maximumBundleV4Version
                                : matchingVersion;
            }

            return matchingVersion?.ToString();
        }

        private NuGetVersion ResolvePlatformReleaseChannelVersion(IList<NuGetVersion> orderedByDescBundles) => _platformReleaseChannel.ToUpper() switch
        {
            ScriptConstants.StandardPlatformChannelNameUpper or ScriptConstants.ExtendedPlatformChannelNameUpper => GetStandardOrExtendedBundleVersion(orderedByDescBundles),
            ScriptConstants.LatestPlatformChannelNameUpper or "" => GetLatestBundleVersion(orderedByDescBundles),
            _ => HandleUnknownPlatformReleaseChannelName(orderedByDescBundles)
        };

        // Standard: Resolves to the version prior to the latest(n-1), if that version is available.
        // Extended: Resolves to the version two releases prior to the latest(n-2), if that version is available.
        // However, Functions and Rapid Update should treat Standard and Extended the same, resolving to n-1.
        private NuGetVersion GetStandardOrExtendedBundleVersion(IList<NuGetVersion> orderedByDescBundlesList)
        {
            var latest = orderedByDescBundlesList.FirstOrDefault();

            if (orderedByDescBundlesList.Count > 1)
            {
                var previous = orderedByDescBundlesList[1];
                _logger.LogInformation("Applying platform release channel configuration {platformReleaseChannelName}. Previous bundle version {previous} will be used instead of latest version {latest}.", _platformReleaseChannel, previous, latest);

                // These channels should resolve to the version prior to latest. This list is in descending order, which makes latest [0], and prior-to-latest [1].
                return previous;
            }

            // keep the latest version, log a notice
            _logger.LogWarning("Unable to apply platform release channel configuration {platformReleaseChannelName}. Only one matching bundle version is available. {latestBundleVersion} will be used", _platformReleaseChannel, latest);
            return latest;
        }

        private NuGetVersion GetLatestBundleVersion(IList<NuGetVersion> orderedByDescBundlesList)
        {
            var latest = orderedByDescBundlesList.FirstOrDefault();
            if (string.Equals(_platformReleaseChannel.ToUpper(), ScriptConstants.LatestPlatformChannelNameUpper))
            {
                _logger.LogInformation("Applying platform release channel configuration {platformReleaseChannelName}. Bundle version {latest} will be used", _platformReleaseChannel, latest);
            }
            return latest;
        }

        private NuGetVersion HandleUnknownPlatformReleaseChannelName(IList<NuGetVersion> orderedByDescBundlesList)
        {
            var latest = GetLatestBundleVersion(orderedByDescBundlesList);
            _logger.LogWarning("Unknown platform release channel name {platformReleaseChannelName}. The latest bundle version, {latestBundleVersion}, will be used.", _platformReleaseChannel, latest);
            return latest;
        }

        public async Task<string> GetExtensionBundleBinPathAsync()
        {
            string bundlePath = await GetExtensionBundlePath();

            if (string.IsNullOrEmpty(bundlePath))
            {
                return null;
            }

            string binPath = string.Empty;

            if (_environment.IsWindowsAzureManagedHosting())
            {
                if (Environment.Is64BitProcess)
                {
                    //bin_v3/win-x64
                    binPath = Path.Combine(bundlePath, ScriptConstants.ExtensionBundleV3BinDirectoryName, ScriptConstants.Windows64BitRID);
                }
                else
                {
                    //bin_v3/win-x86
                    binPath = Path.Combine(bundlePath, ScriptConstants.ExtensionBundleV3BinDirectoryName, ScriptConstants.Windows32BitRID);
                }
            }

            if (_environment.IsLinuxAzureManagedHosting())
            {
                // linux only has 64 bit version of process - bin_v3/linux-x64
                binPath = Path.Combine(bundlePath, ScriptConstants.ExtensionBundleV3BinDirectoryName, ScriptConstants.Linux64BitRID);
            }

            // Check if RR direcory exist if not fallback to non RR binaries
            binPath = FileUtility.DirectoryExists(binPath) ? binPath : Path.Combine(bundlePath, "bin");

            // if no bin directory is present something is wrong
            return FileUtility.DirectoryExists(binPath) ? binPath : null;
        }

        public string GetOutdatedBundleVersion()
        {
            // If the extension bundle version is not set or if the extension bundle is not the default one,
            // return empty string
            if (string.IsNullOrEmpty(_extensionBundleVersion) ||
                !string.Equals(_options?.Id, ScriptConstants.DefaultExtensionBundleId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Extract the major version number from the version string
            int dotIndex = _extensionBundleVersion.IndexOf('.');
            if (dotIndex <= 0 || !int.TryParse(_extensionBundleVersion.AsSpan(0, dotIndex), out var majorVersion) || majorVersion == 0)
            {
                return null;
            }

            int latestMajorVersion = ScriptConstants.ExtensionBundleV4MajorVersion;

            // Return the version if it's outdated
            if (majorVersion < latestMajorVersion)
            {
                return _extensionBundleVersion;
            }

            return null;
        }
    }
}