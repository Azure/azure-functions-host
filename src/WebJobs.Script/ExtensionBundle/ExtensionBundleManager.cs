// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle
{
    public class ExtensionBundleManager : IExtensionBundleManager
    {
        private readonly IEnvironment _environment;
        private readonly ExtensionBundleOptions _options;
        private readonly ILogger _logger;
        private readonly string _cdnUri;

        public ExtensionBundleManager(ExtensionBundleOptions options, IEnvironment environment, ILoggerFactory loggerFactory)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = loggerFactory.CreateLogger<ExtensionBundleManager>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            _cdnUri = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ExtensionBundleSourceUri) ?? ScriptConstants.ExtensionBundleDefaultSourceUri;
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public bool IsExtensionBundleConfigured()
        {
            return !string.IsNullOrEmpty(_options.Id) && !string.IsNullOrEmpty(_options.Version?.OriginalString);
        }

        /// <summary>
        /// Attempts to locate the extension bundle inside the probing paths and download paths. If the extension bundle is not found then it will download the extension bundle.
        /// </summary>
        /// <returns>Path of the extension bundle</returns>
        public async Task<string> GetExtensionBundlePath()
        {
            using (var httpClient = new HttpClient())
            {
                return await GetBundle(httpClient);
            }
        }

        /// <summary>
        /// Attempts to locate the extension bundle inside the probing paths and download paths. If the extension bundle is not found then it will download the extension bundle.
        /// </summary>
        /// <param name="httpClient">HttpClient used to download the extension bundle</param>
        /// <returns>Path of the extension bundle</returns>
        public async Task<string> GetExtensionBundlePath(HttpClient httpClient)
        {
            return await GetBundle(httpClient);
        }

        private async Task<string> GetBundle(HttpClient httpClient)
        {
            string latestBundleVersion = await GetLatestMatchingBundleVersion(httpClient);
            if (string.IsNullOrEmpty(latestBundleVersion))
            {
                return null;
            }

            bool bundleFound = TryLocateExtensionBundle(out string bundlePath);
            string bundleVersion = Path.GetFileName(bundlePath);

            if (_environment.IsPersistentFileSystemAvailable()
                && (!bundleFound || (Version.Parse(bundleVersion) < Version.Parse(latestBundleVersion) && _options.EnsureLatest)))
            {
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
                _logger.LogInformation(Resources.LocateExtensionBundle, _options.Id, path);
                if (FileUtility.DirectoryExists(path))
                {
                    var bundleDirectories = FileUtility.EnumerateDirectories(path);
                    string version = FindBestVersionMatch(_options.Version, bundleDirectories);

                    if (!string.IsNullOrEmpty(version))
                    {
                        bundlePath = Path.Combine(path, version);
                        bundleMetatdataFile = Path.Combine(bundlePath, ScriptConstants.ExtensionBundleMetadatFile);
                        if (!string.IsNullOrEmpty(bundleMetatdataFile) && FileUtility.FileExists(bundleMetatdataFile))
                        {
                            _logger.LogInformation(Resources.ExtensionBundleFound, bundlePath);
                            break;
                        }
                    }
                }
            }
            return bundlePath != null;
        }

        private async Task<string> DownloadExtensionBundleAsync(string version, HttpClient httpClient)
        {
            string zipDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            FileUtility.EnsureDirectoryExists(zipDirectoryPath);

            string zipFilePath = Path.Combine(zipDirectoryPath, $"{version}.zip");
            var zipUri = new Uri($"{_cdnUri}/{ScriptConstants.ExtensionBundleDirectory}/{_options.Id}/{version}/{version}.zip");

            string bundleMetatdataFile = null;
            string bundlePath = null;
            if (await TryDownloadZipFileAsync(zipUri, zipFilePath, httpClient))
            {
                bundlePath = Path.Combine(_options.DownloadPath, version);
                FileUtility.EnsureDirectoryExists(bundlePath);

                _logger.LogInformation(Resources.ExtractingBundleZip, bundlePath);
                ZipFile.ExtractToDirectory(zipFilePath, bundlePath);
                _logger.LogInformation(Resources.ZipExtractionComplete);

                bundleMetatdataFile = Path.Combine(_options.DownloadPath, version, ScriptConstants.ExtensionBundleMetadatFile);
            }
            return FileUtility.FileExists(bundleMetatdataFile) ? bundlePath : null;
        }

        private async Task<bool> TryDownloadZipFileAsync(Uri zipUri, string filePath, HttpClient httpClient)
        {
            _logger.LogInformation(Resources.DownloadingZip, zipUri, filePath);
            var response = await httpClient.GetAsync(zipUri);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(Resources.ErrorDownloadingZip, zipUri, response.StatusCode, response.ReasonPhrase);
                return false;
            }

            using (var content = await response.Content.ReadAsStreamAsync())
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await content.CopyToAsync(stream);
            }

            _logger.LogInformation(Resources.DownloadComplete, zipUri, filePath);
            return true;
        }

        private async Task<string> GetLatestMatchingBundleVersion(HttpClient httpClient)
        {
            var uri = new Uri($"{_cdnUri}/{ScriptConstants.ExtensionBundleDirectory}/{_options.Id}/{ScriptConstants.ExtensionBundleVersionIndexFile}");
            _logger.LogInformation(Resources.FetchingVersionInfo, _options.Id, uri.Authority, uri.AbsolutePath);

            var response = await httpClient.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(Resources.ErrorFetchingVersionInfo, _options.Id);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var bundleVersions = JsonConvert.DeserializeObject<IEnumerable<string>>(content);
            var matchingBundleVersion = FindBestVersionMatch(_options.Version, bundleVersions);

            if (string.IsNullOrEmpty(matchingBundleVersion))
            {
                _logger.LogInformation(Resources.MatchingBundleNotFound, _options.Version);
            }

            return matchingBundleVersion;
        }

        private static string FindBestVersionMatch(VersionRange versionRange, IEnumerable<string> versions)
        {
            var bundleVersions = versions.Select(p =>
            {
                var dirName = Path.GetFileName(p);
                NuGetVersion.TryParse(dirName, out NuGetVersion version);
                return version;
            }).Where(v => v != null);

            return versionRange.FindBestMatch(bundleVersions)?.ToString();
        }
    }
}