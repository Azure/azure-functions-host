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
using Microsoft.Azure.WebJobs.Script.Conditions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Models;
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
        private readonly BundleRequirementsEvaluator _requirementsEvaluator;
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
            var conditionProvider = new BundleConditionProvider(_logger, _environment, SystemRuntimeInformation.Instance);
            _requirementsEvaluator = new BundleRequirementsEvaluator(conditionProvider, _logger);
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
        public async Task<string> GetExtensionBundlePath()
        {
            var client = _httpClientFactory.CreateClient(ExtensionBundleClientName);
            return await GetBundle(client);
        }

        /// <summary>
        /// Attempts to locate the extension bundle inside the probing paths and download paths. If the extension bundle is not found then it will download the extension bundle.
        /// </summary>
        public async Task<string> GetExtensionBundlePath(HttpClient httpClient)
        {
            return await GetBundle(httpClient);
        }

        private async Task<string> GetBundle(HttpClient httpClient)
        {
            bool bundleFound = TryLocateExtensionBundle(out string bundlePath);

            // CDN is consulted when:
            //   - host is in a CDN-eligible environment, AND
            //   - either no local bundle satisfied requirements, OR EnsureLatest forces a check.
            bool cdnEligible = _environment.IsAppService()
                || _environment.IsCoreTools()
                || _environment.IsAnyLinuxConsumption()
                || _environment.IsContainer();

            if (cdnEligible && (!bundleFound || _options.EnsureLatest))
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
            var paths = new List<string>(_options.ProbingPaths)
                {
                    _options.DownloadPath
                };

            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                _logger.LocateExtensionBundle(_options.Id, path);
                if (!FileUtility.DirectoryExists(path))
                {
                    continue;
                }

                var bundleDirectories = FileUtility.EnumerateDirectories(path);
                var matchingVersions = GetMatchingVersionsDescending(bundleDirectories);
                if (matchingVersions.Count == 0)
                {
                    continue;
                }

                int startIndex = ResolveStartIndex(matchingVersions);
                for (int v = startIndex; v < matchingVersions.Count; v++)
                {
                    string candidateVersion = ApplyMaxVersionCap(matchingVersions[v])?.ToString();
                    if (string.IsNullOrEmpty(candidateVersion))
                    {
                        continue;
                    }

                    string candidatePath = Path.Combine(path, candidateVersion);
                    string metadataFile = Path.Combine(candidatePath, ScriptConstants.ExtensionBundleMetadataFile);
                    if (!FileUtility.FileExists(metadataFile))
                    {
                        _logger.LogDebug("Bundle candidate at '{path}' has no bundle.json; skipping.", candidatePath);
                        continue;
                    }

                    if (_requirementsEvaluator.EvaluateFromFile(metadataFile, _options.Id, candidateVersion))
                    {
                        bundlePath = candidatePath;
                        _logger.ExtensionBundleFound(bundlePath);
                        return true;
                    }

                    _logger.LogDebug("Local bundle v{version} at '{path}' did not meet requirements. Trying next version.", candidateVersion, candidatePath);
                }
            }
            return false;
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

            var matchingVersions = GetMatchingVersionsDescending(bundleVersions);
            if (matchingVersions.Count == 0)
            {
                _logger.MatchingBundleNotFound(_options.Version.OriginalString);
                return null;
            }

            int startIndex = ResolveStartIndex(matchingVersions);
            for (int v = startIndex; v < matchingVersions.Count; v++)
            {
                string candidateVersion = ApplyMaxVersionCap(matchingVersions[v])?.ToString();
                if (string.IsNullOrEmpty(candidateVersion))
                {
                    continue;
                }

                if (await EvaluateCdnBundleRequirementsAsync(candidateVersion, httpClient))
                {
                    return candidateVersion;
                }

                _logger.LogInformation("CDN bundle '{id}' v{version} did not meet requirements. Trying next version.", _options.Id, candidateVersion);
            }

            _logger.MatchingBundleNotFound(_options.Version.OriginalString);
            return null;
        }

        private async Task<bool> EvaluateCdnBundleRequirementsAsync(string version, HttpClient httpClient)
        {
            var bundleJsonUri = new Uri($"{_cdnUri}/{ScriptConstants.ExtensionBundleDirectory}/{_options.Id}/{version}/{ScriptConstants.ExtensionBundleMetadataFile}");
            _logger.LogDebug("Fetching bundle.json for requirements evaluation: {uri}", bundleJsonUri);

            try
            {
                using var response = await httpClient.GetAsync(bundleJsonUri);
                if (!response.IsSuccessStatusCode)
                {
                    // If bundle.json can't be fetched, treat as "no requirements" (backward compat).
                    _logger.LogWarning("Unable to fetch bundle.json at '{uri}' (status {status}); assuming no requirements.", bundleJsonUri, response.StatusCode);
                    return true;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                return await _requirementsEvaluator.EvaluateFromStreamAsync(stream, _options.Id, version);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
            {
                _logger.LogWarning(ex, "Error fetching bundle.json for '{id}' v{version}; assuming no requirements.", _options.Id, version);
                return true;
            }
        }

        /// <summary>
        /// Returns versions that satisfy the configured version range, sorted descending.
        /// </summary>
        internal List<NuGetVersion> GetMatchingVersionsDescending(IEnumerable<string> versions)
        {
            return versions
                .Select(p =>
                {
                    var dirName = Path.GetFileName(p);
                    NuGetVersion.TryParse(dirName, out NuGetVersion version);
                    if (version != null && !_options.Version.Satisfies(version))
                    {
                        version = null;
                    }
                    return version;
                })
                .Where(v => v != null)
                .OrderByDescending(v => v.Version)
                .ToList();
        }

        /// <summary>
        /// Applies release channel policy to determine the starting index into the descending-sorted
        /// list of matching versions. LATEST → 0; STANDARD/EXTENDED → 1 (n-1) when >1 available, else 0.
        /// </summary>
        internal int ResolveStartIndex(IList<NuGetVersion> orderedByDescBundles)
        {
            if (orderedByDescBundles.Count == 0)
            {
                return 0;
            }

            switch (_platformReleaseChannel?.ToUpperInvariant())
            {
                case ScriptConstants.StandardPlatformChannelNameUpper:
                case ScriptConstants.ExtendedPlatformChannelNameUpper:
                    if (orderedByDescBundles.Count > 1)
                    {
                        _logger.LogInformation("Applying platform release channel configuration {platformReleaseChannelName}. Will start from index 1 (n-1).", _platformReleaseChannel);
                        return 1;
                    }
                    _logger.LogWarning("Unable to apply platform release channel configuration {platformReleaseChannelName}. Only one matching bundle version is available.", _platformReleaseChannel);
                    return 0;

                case ScriptConstants.LatestPlatformChannelNameUpper:
                case "":
                case null:
                    return 0;

                default:
                    _logger.LogWarning("Unknown platform release channel name {platformReleaseChannelName}. Starting from latest.", _platformReleaseChannel);
                    return 0;
            }
        }

        /// <summary>
        /// Applies the hosting-config maximum version cap for the default bundle id.
        /// Non-default bundles or when no cap configured → returns the input version unchanged.
        /// </summary>
        private NuGetVersion ApplyMaxVersionCap(NuGetVersion candidate)
        {
            if (candidate == null || _options.Id != ScriptConstants.DefaultExtensionBundleId)
            {
                return candidate;
            }

            if (!string.IsNullOrEmpty(_configOption.MaximumBundleV3Version)
                && candidate.Major == ScriptConstants.ExtensionBundleV3MajorVersion)
            {
                var cap = NuGetVersion.Parse(_configOption.MaximumBundleV3Version);
                return candidate > cap ? cap : candidate;
            }

            if (!string.IsNullOrEmpty(_configOption.MaximumBundleV4Version)
                && candidate.Major == ScriptConstants.ExtensionBundleV4MajorVersion)
            {
                var cap = NuGetVersion.Parse(_configOption.MaximumBundleV4Version);
                return candidate > cap ? cap : candidate;
            }

            return candidate;
        }

        /// <summary>
        /// Legacy single-best version selector. Kept for tests that exercise version selection
        /// without requirements evaluation.
        /// </summary>
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

        private NuGetVersion GetStandardOrExtendedBundleVersion(IList<NuGetVersion> orderedByDescBundlesList)
        {
            var latest = orderedByDescBundlesList.FirstOrDefault();

            if (orderedByDescBundlesList.Count > 1)
            {
                var previous = orderedByDescBundlesList[1];
                _logger.LogInformation("Applying platform release channel configuration {platformReleaseChannelName}. Previous bundle version {previous} will be used instead of latest version {latest}.", _platformReleaseChannel, previous, latest);
                return previous;
            }

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
                    binPath = Path.Combine(bundlePath, ScriptConstants.ExtensionBundleV3BinDirectoryName, ScriptConstants.Windows64BitRID);
                }
                else
                {
                    binPath = Path.Combine(bundlePath, ScriptConstants.ExtensionBundleV3BinDirectoryName, ScriptConstants.Windows32BitRID);
                }
            }

            if (_environment.IsLinuxAzureManagedHosting())
            {
                binPath = Path.Combine(bundlePath, ScriptConstants.ExtensionBundleV3BinDirectoryName, ScriptConstants.Linux64BitRID);
            }

            binPath = FileUtility.DirectoryExists(binPath) ? binPath : Path.Combine(bundlePath, "bin");

            return FileUtility.DirectoryExists(binPath) ? binPath : null;
        }

        public string GetOutdatedBundleVersion()
        {
            if (string.IsNullOrEmpty(_extensionBundleVersion) ||
                !string.Equals(_options?.Id, ScriptConstants.DefaultExtensionBundleId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            int dotIndex = _extensionBundleVersion.IndexOf('.');
            if (dotIndex <= 0 || !int.TryParse(_extensionBundleVersion.AsSpan(0, dotIndex), out var majorVersion) || majorVersion == 0)
            {
                return null;
            }

            int latestMajorVersion = ScriptConstants.ExtensionBundleV4MajorVersion;

            if (majorVersion < latestMajorVersion)
            {
                return _extensionBundleVersion;
            }

            return null;
        }
    }
}
