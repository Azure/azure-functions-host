// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions
{
    internal static class ExtensionBundleLoggerExtension
    {
        // EventId range is 100-199

        private static readonly Action<ILogger, string, Exception> _contentProviderNotConfigured =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(100, nameof(ContentProviderNotConfigured)),
            "Extension bundle configuration is not present in host.json.Cannot load content for file {path}");

        private static readonly Action<ILogger, string, Exception> _contentFileNotFound =
            LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(101, nameof(ContentFileNotFound)),
            "File not found at {contentPath}.");

        private static readonly Action<ILogger, string, string, Exception> _locateExtensionBundle =
            LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(102, nameof(LocateExtensionBundle)),
            "Looking for extension bundle {id} at {path}");

        private static readonly Action<ILogger, string, Exception> _extensionBundleFound =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(103, nameof(ExtensionBundleFound)),
            "Found a matching extension bundle at {bundlePath}");

        private static readonly Action<ILogger, string, Exception> _extractingBundleZip =
            LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(104, nameof(ExtractingBundleZip)),
            "Extracting extension bundle at {bundlePath}");

        private static readonly Action<ILogger, Exception> _zipExtractionComplete =
            LoggerMessage.Define(
            LogLevel.Information,
            new EventId(105, nameof(ZipExtractionComplete)),
            "Zip extraction complete");

        private static readonly Action<ILogger, string, string, Exception> _downloadingZip =
            LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(106, nameof(DownloadingZip)),
            "Downloading extension bundle from {zipUri} to {filePath}");

        private static readonly Action<ILogger, string, string, string, Exception> _errorDownloadingZip =
            LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            new EventId(107, nameof(ErrorDownloadingZip)),
            "Error downloading zip content {zip}. Status Code:{statusCode}. Reason:{reasonPhrase}");

        private static readonly Action<ILogger, string, string, Exception> _downloadComplete =
            LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(108, nameof(DownloadComplete)),
            "Completed downloading extension bundle from {zip} to {filePath}");

        private static readonly Action<ILogger, string, string, Exception> _fetchingVersionInfo =
            LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(109, nameof(FetchingVersionInfo)),
            "Fetching information on versions of extension bundle {id} available on {uriString}");

        private static readonly Action<ILogger, string, Exception> _errorFetchingVersionInfo =
            LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(110, nameof(ErrorFetchingVersionInfo)),
            "Error fetching version information for extension bundle {id}");

        private static readonly Action<ILogger, string, Exception> _matchingBundleNotFound =
            LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(111, nameof(MatchingBundleNotFound)),
            "Bundle version matching the {version} was not found");

        private static readonly Action<ILogger, Uri, HttpStatusCode?, HttpRequestError?, string, string, string, Exception> _errorDownloadingExtensionBundleZipHttpRequest =
            LoggerMessage.Define<Uri, HttpStatusCode?, HttpRequestError?, string, string, string>(
            LogLevel.Error,
            new EventId(112, nameof(ErrorDownloadingExtensionBundleZipHttpRequest)),
            "Error downloading extension bundle zip content {zip}. Status Code:{statusCode}, RequestError:{requestError}, FilePath:{filePath}, Disk:{diskUsage}, AzureRef:{azureRef}");

        // Logs unexpected (non-HttpRequestException) failures during bundle download. No HTTP status/request error data.
        private static readonly Action<ILogger, Uri, string, string, string, Exception> _errorDownloadingExtensionBundleZipUnexpected =
            LoggerMessage.Define<Uri, string, string, string>(
            LogLevel.Error,
            new EventId(113, nameof(ErrorDownloadingExtensionBundleZipUnexpected)),
            "Unexpected error downloading extension bundle Zip content {zip}. FilePath:{filePath}, Disk:{diskUsage}, AzureRef:{azureRef}");

        // Logs IO-specific failures (e.g., disk full, device I/O issues) during bundle download.
        private static readonly Action<ILogger, Uri, string, string, string, Exception> _errorDownloadingExtensionBundleZipIO =
            LoggerMessage.Define<Uri, string, string, string>(
            LogLevel.Error,
            new EventId(114, nameof(ErrorDownloadingExtensionBundleZipIO)),
            "IO error downloading extension bundle Zip content {zip}. FilePath:{filePath}, Disk:{diskUsage}, AzureRef:{azureRef}");

        public static void ContentProviderNotConfigured(this ILogger logger, string path)
        {
            _contentProviderNotConfigured(logger, path, null);
        }

        public static void ContentFileNotFound(this ILogger logger, string contentFilePath)
        {
            _contentFileNotFound(logger, contentFilePath, null);
        }

        public static void LocateExtensionBundle(this ILogger logger, string id, string path)
        {
            _locateExtensionBundle(logger, id, path, null);
        }

        public static void ExtensionBundleFound(this ILogger logger, string bundlePath)
        {
            _extensionBundleFound(logger, bundlePath, null);
        }

        public static void ExtractingBundleZip(this ILogger logger, string bundlePath)
        {
             _extractingBundleZip(logger, bundlePath, null);
        }

        public static void ZipExtractionComplete(this ILogger logger)
        {
            _zipExtractionComplete(logger, null);
        }

        public static void DownloadingZip(this ILogger logger, Uri zipUri, string filePath)
        {
            string zip = zipUri.ToString();
            _downloadingZip(logger, zip, filePath, null);
        }

        public static void ErrorDownloadingZip(this ILogger logger, Uri zipUri, HttpResponseMessage response)
        {
            string zip = zipUri.ToString();
            string statusCode = response.StatusCode.ToString();
            string reasonPhrase = response.ReasonPhrase.ToString();
            _errorDownloadingZip(logger, zip, statusCode, reasonPhrase, null);
        }

        public static void ErrorDownloadingExtensionBundleZipHttpRequest(this ILogger logger, Exception ex, Uri zipUri, HttpStatusCode? statusCode, HttpRequestError? httpRequestError, string filePath, string diskUsage, string azureRef)
        {
            // Avoid premature ToString allocations; LoggerMessage will format only if enabled.
            _errorDownloadingExtensionBundleZipHttpRequest(logger, zipUri, statusCode, httpRequestError, filePath, diskUsage, azureRef, ex);
        }

        public static void ErrorDownloadingExtensionBundleZipUnexpected(this ILogger logger, Exception ex, Uri zipUri, string filePath, string diskUsage, string azureRef)
        {
            // Generic non-HTTP failure (e.g. IO, disk full, zip extraction issues).
            _errorDownloadingExtensionBundleZipUnexpected(logger, zipUri, filePath, diskUsage, azureRef, ex);
        }

        public static void ErrorDownloadingExtensionBundleZipIO(this ILogger logger, Exception ex, Uri zipUri, string filePath, string diskUsage, string azureRef)
        {
            _errorDownloadingExtensionBundleZipIO(logger, zipUri, filePath, diskUsage, azureRef, ex);
        }

        public static void DownloadComplete(this ILogger logger, Uri zipUri, string filePath)
        {
            string zip = zipUri.ToString();
            _downloadComplete(logger, zip, filePath, null);
        }

        public static void FetchingVersionInfo(this ILogger logger, string id, Uri uri)
        {
            string uriString = uri.ToString();
            _fetchingVersionInfo(logger, id, uriString, null);
        }

        public static void ErrorFetchingVersionInfo(this ILogger logger, string id)
        {
            _errorFetchingVersionInfo(logger, id, null);
        }

        public static void MatchingBundleNotFound(this ILogger logger, string version)
        {
            _matchingBundleNotFound(logger, version, null);
        }
    }
}
