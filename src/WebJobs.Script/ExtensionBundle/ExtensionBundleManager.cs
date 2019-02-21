// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Properties;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle
{
    public class ExtensionBundleManager : IExtensionBundleManager
    {
        private readonly IEnvironment _environment;
        private readonly IOptions<ExtensionBundleOptions> _options;
        private readonly ILogger _logger;
        private readonly string _cdnUri;

        public ExtensionBundleManager(IEnvironment environment,
                                      IOptions<ExtensionBundleOptions> extensionBundleOptions,
                                      ILogger<ExtensionBundleManager> logger)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _options = extensionBundleOptions ?? throw new ArgumentNullException(nameof(extensionBundleOptions));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cdnUri = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AlternateCdnUri) ?? ScriptConstants.ExtensionBundleCdnUri;
        }

        public bool IsExtensionBundleConfigured()
        {
            return !string.IsNullOrEmpty(_options.Value.Id) && !string.IsNullOrEmpty(_options.Value.Version?.OriginalString);
        }

        public async Task<string> GetExtensionBundle(HttpClient httpClient = null)
        {
            httpClient = httpClient ?? new HttpClient();
            using (httpClient)
            {
                string latestBundleVersion = await GetLatestMatchingBundleVersion(httpClient);
                if (string.IsNullOrEmpty(latestBundleVersion))
                {
                    return null;
                }

                bool bundleFound = TryLocateExtensionBundle(out string bundlePath);
                string bundleVersion = Path.GetFileName(bundlePath);

                if (!bundleFound || (Version.Parse(bundleVersion) < Version.Parse(latestBundleVersion) && _options.Value.EnsureLatest))
                {
                    bundlePath = await DownloadExtensionBundleAsync(latestBundleVersion, httpClient);
                }

                return bundlePath;
            }
        }

        internal bool TryLocateExtensionBundle(out string bundlePath)
        {
            bundlePath = null;
            string bundleMetatdataFile = null;
            var paths = new List<string>(_options.Value.ProbingPaths)
            {
                _options.Value.DownloadPath
            };

            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                _logger.LogInformation(Resources.LocateExtensionBundle, _options.Value.Id, path);
                if (FileUtility.DirectoryExists(path))
                {
                    var bundleDirectories = FileUtility.EnumerateDirectories(path);
                    string version = FindBestVersionMatch(_options.Value.Version, bundleDirectories);

                    if (!string.IsNullOrEmpty(version))
                    {
                        bundlePath = Path.Combine(path, version);
                        bundleMetatdataFile = Path.Combine(bundlePath, ScriptConstants.ExtensionBundleMetadatFile);
                        _logger.LogInformation(Resources.ExtensionBundleFound, bundlePath);
                        break;
                    }
                }
            }

            return !string.IsNullOrEmpty(bundleMetatdataFile) && FileUtility.FileExists(bundleMetatdataFile);
        }

        private async Task<string> DownloadExtensionBundleAsync(string version, HttpClient httpClient)
        {
            string zipDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            FileUtility.EnsureDirectoryExists(zipDirectoryPath);

            string zipFilePath = Path.Combine(zipDirectoryPath, $"{version}.zip");
            var zipUri = new Uri($"{_cdnUri}/{_options.Value.Id}/{version}/{version}.zip");

            string bundleMetatdataFile = null;
            string bundlePath = null;
            if (await TryDownloadZipFileAsync(zipUri, zipFilePath, httpClient))
            {
                bundlePath = Path.Combine(_options.Value.DownloadPath, version);
                FileUtility.EnsureDirectoryExists(bundlePath);

                _logger.LogInformation(Resources.ExtractingBundleZip, bundlePath);
                ZipFile.ExtractToDirectory(zipFilePath, bundlePath);
                _logger.LogInformation(Resources.ZipExtractionComplete);

                bundleMetatdataFile = Path.Combine(_options.Value.DownloadPath, version, ScriptConstants.ExtensionBundleMetadatFile);
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
            var uri = new Uri($"{_cdnUri}/{_options.Value.Id}/{ScriptConstants.ExtensionBundleVersionIndexFile}");
            _logger.LogInformation(Resources.FetchingVersionInfo, _options.Value.Id, uri.Authority, uri.AbsolutePath);

            var response = await httpClient.GetAsync(uri);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(Resources.ErrorFetchingVersionInfo, _options.Value.Id);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var bundleVersions = JsonConvert.DeserializeObject<IEnumerable<string>>(content);
            var matchingBundleVersion = FindBestVersionMatch(_options.Value.Version, bundleVersions);

            if (string.IsNullOrEmpty(matchingBundleVersion))
            {
                _logger.LogInformation(Resources.MatchingBundleNotFound, _options.Value.Version);
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