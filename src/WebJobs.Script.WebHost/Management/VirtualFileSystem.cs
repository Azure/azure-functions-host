// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class VirtualFileSystem
    {
        public const char UriSegmentSeparator = '/';
        private const string DirectoryEnumerationSearchPattern = "*";
        private const string DummyRazorExtension = ".func777";

        private static readonly char[] _uriSegmentSeparator = new char[] { UriSegmentSeparator };
        private static readonly MediaTypeHeaderValue _directoryMediaType = MediaTypeHeaderValue.Parse("inode/directory");

        protected const int BufferSize = 32 * 1024;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
        private readonly ILogger _logger;

        public VirtualFileSystem(IOptionsMonitor<ScriptApplicationHostOptions> options, ILoggerFactory loggerFactory)
        {
            _options = options;
            _logger = loggerFactory.CreateLogger<VirtualFileSystem>();
            MediaTypeMap = MediaTypeMap.Default;
        }

        protected string RootPath
        {
            get
            {
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath) ?? Path.GetFullPath(_options.CurrentValue.ScriptPath)
                    : Path.DirectorySeparatorChar.ToString();
            }
        }

        protected MediaTypeMap MediaTypeMap { get; private set; }

        public virtual Task<HttpResponseMessage> GetItem(HttpRequest request)
        {
            string localFilePath = GetLocalFilePath(request);

            if (VfsSpecialFolders.TryHandleRequest(request, localFilePath, out HttpResponseMessage response))
            {
                return Task.FromResult(response);
            }

            var info = FileUtility.DirectoryInfoFromDirectoryName(localFilePath);

            if (info.Attributes < 0)
            {
                var notFoundResponse = CreateResponse(HttpStatusCode.NotFound, string.Format("'{0}' not found.", info.FullName));
                return Task.FromResult(notFoundResponse);
            }
            else if ((info.Attributes & FileAttributes.Directory) != 0)
            {
                // If request URI does NOT end in a "/" then redirect to one that does
                var uri = request.GetRequestUri();
                if (!uri.AbsolutePath.EndsWith("/"))
                {
                    var redirectResponse = CreateResponse(HttpStatusCode.TemporaryRedirect);
                    var location = new UriBuilder(uri);
                    location.Path += "/";
                    redirectResponse.Headers.Location = location.Uri;
                    return Task.FromResult(redirectResponse);
                }
                else
                {
                    return CreateDirectoryGetResponse(request, info, localFilePath);
                }
            }
            else
            {
                // If request URI ends in a "/" then redirect to one that does not
                if (localFilePath[localFilePath.Length - 1] == Path.DirectorySeparatorChar)
                {
                    HttpResponseMessage redirectResponse = CreateResponse(HttpStatusCode.TemporaryRedirect);
                    UriBuilder location = new UriBuilder(request.GetRequestUri());
                    location.Path = location.Path.TrimEnd(_uriSegmentSeparator);
                    redirectResponse.Headers.Location = location.Uri;
                    return Task.FromResult(redirectResponse);
                }

                // We are ready to get the file
                return CreateItemGetResponse(request, info, localFilePath);
            }
        }

        public virtual Task<HttpResponseMessage> PutItem(HttpRequest request)
        {
            var localFilePath = GetLocalFilePath(request);

            if (VfsSpecialFolders.TryHandleRequest(request, localFilePath, out HttpResponseMessage response))
            {
                return Task.FromResult(response);
            }

            var info = FileUtility.DirectoryInfoFromDirectoryName(localFilePath);
            var itemExists = info.Attributes >= 0;

            if (itemExists && (info.Attributes & FileAttributes.Directory) != 0)
            {
                return CreateDirectoryPutResponse(request, info, localFilePath);
            }
            else
            {
                // If request URI ends in a "/" then attempt to create the directory.
                if (localFilePath[localFilePath.Length - 1] == Path.DirectorySeparatorChar)
                {
                    return CreateDirectoryPutResponse(request, info, localFilePath);
                }

                // We are ready to update the file
                return CreateItemPutResponse(request, info, localFilePath, itemExists);
            }
        }

        public virtual Task<HttpResponseMessage> DeleteItem(HttpRequest request, bool recursive = false)
        {
            string localFilePath = GetLocalFilePath(request);

            if (VfsSpecialFolders.TryHandleRequest(request, localFilePath, out HttpResponseMessage response))
            {
                return Task.FromResult(response);
            }

            var dirInfo = FileUtility.DirectoryInfoFromDirectoryName(localFilePath);

            if (dirInfo.Attributes < 0)
            {
                var notFoundResponse = CreateResponse(HttpStatusCode.NotFound, string.Format("'{0}' not found.", dirInfo.FullName));
                return Task.FromResult(notFoundResponse);
            }
            else if ((dirInfo.Attributes & FileAttributes.Directory) != 0)
            {
                try
                {
                    dirInfo.Delete(recursive);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);
                    var conflictDirectoryResponse = CreateResponse(HttpStatusCode.Conflict, ex);
                    return Task.FromResult(conflictDirectoryResponse);
                }

                // Delete directory succeeded.
                var successResponse = CreateResponse(HttpStatusCode.OK);
                return Task.FromResult(successResponse);
            }
            else
            {
                // If request URI ends in a "/" then redirect to one that does not
                if (localFilePath[localFilePath.Length - 1] == Path.DirectorySeparatorChar)
                {
                    var redirectResponse = CreateResponse(HttpStatusCode.TemporaryRedirect);
                    var location = new UriBuilder(request.GetRequestUri());
                    location.Path = location.Path.TrimEnd(_uriSegmentSeparator);
                    redirectResponse.Headers.Location = location.Uri;
                    return Task.FromResult(redirectResponse);
                }

                // We are ready to delete the file
                var fileInfo = FileUtility.FileInfoFromFileName(localFilePath);
                return CreateFileDeleteResponse(request, fileInfo);
            }
        }

        public static Uri FilePathToVfsUri(string filePath, string baseUrl, ScriptJobHostOptions config, bool isDirectory = false)
        {
            var home = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath) ?? config.RootScriptPath
                : Path.DirectorySeparatorChar.ToString();

            filePath = filePath
                .Substring(home.Length)
                .Trim('\\', '/')
                .Replace("\\", "/");

            return new Uri($"{baseUrl}/admin/vfs/{filePath}{(isDirectory ? "/" : string.Empty)}");
        }

        public static string VfsUriToFilePath(Uri uri, ScriptJobHostOptions config, bool isDirectory = false)
        {
            if (uri != null)
            {
                var home = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath) ?? config.RootScriptPath
                : Path.DirectorySeparatorChar.ToString();

                var filePath = uri.AbsolutePath.Split("/admin/vfs").LastOrDefault();
                filePath = string.IsNullOrEmpty(filePath)
                    ? home
                    : Path.Combine(home, filePath.TrimStart('/'));

                return filePath.Replace('/', Path.DirectorySeparatorChar);
            }
            else
            {
                return null;
            }
        }

        protected virtual Task<HttpResponseMessage> CreateDirectoryGetResponse(HttpRequest request, DirectoryInfoBase info, string localFilePath)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            try
            {
                // Enumerate directory
                var directory = GetDirectoryResponse(request, info.GetFileSystemInfos());
                var successDirectoryResponse = CreateResponse(HttpStatusCode.OK, directory);
                return Task.FromResult(successDirectoryResponse);
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                HttpResponseMessage errorResponse = CreateResponse(HttpStatusCode.InternalServerError, e.Message);
                return Task.FromResult(errorResponse);
            }
        }

        protected Task<HttpResponseMessage> CreateItemGetResponse(HttpRequest request, FileSystemInfoBase info, string localFilePath)
        {
            // Get current etag
            var currentEtag = CreateEntityTag(info);
            var lastModified = info.LastWriteTimeUtc;

            // Check whether we have a range request (taking If-Range condition into account)
            bool isRangeRequest = IsRangeRequest(request, currentEtag);

            // Check whether we have a conditional If-None-Match request
            // Unless it is a range request (see RFC2616 sec 14.35.2 Range Retrieval Requests)
            if (!isRangeRequest && IsIfNoneMatchRequest(request, currentEtag.ToSystemETag()))
            {
                var notModifiedResponse = CreateResponse(HttpStatusCode.NotModified);
                notModifiedResponse.SetEntityTagHeader(currentEtag.ToSystemETag(), lastModified);
                return Task.FromResult(notModifiedResponse);
            }

            // Generate file response
            Stream fileStream = null;
            try
            {
                fileStream = GetFileReadStream(localFilePath);
                var mediaType = MediaTypeMap.GetMediaType(info.Extension);
                var successFileResponse = CreateResponse(isRangeRequest ? HttpStatusCode.PartialContent : HttpStatusCode.OK);

                if (isRangeRequest)
                {
                    var typedHeaders = request.GetTypedHeaders();
                    var rangeHeader = new RangeHeaderValue
                    {
                        Unit = typedHeaders.Range.Unit.Value
                    };

                    foreach (var range in typedHeaders.Range.Ranges)
                    {
                        rangeHeader.Ranges.Add(new RangeItemHeaderValue(range.From, range.To));
                    }

                    successFileResponse.Content = new ByteRangeStreamContent(fileStream, rangeHeader, mediaType, BufferSize);
                }
                else
                {
                    successFileResponse.Content = new StreamContent(fileStream, BufferSize);
                    successFileResponse.Content.Headers.ContentType = mediaType;
                }

                // Set etag for the file
                successFileResponse.SetEntityTagHeader(currentEtag.ToSystemETag(), lastModified);
                return Task.FromResult(successFileResponse);
            }
            catch (InvalidByteRangeException invalidByteRangeException)
            {
                // The range request had no overlap with the current extend of the resource so generate a 416 (Requested Range Not Satisfiable)
                // including a Content-Range header with the current size.
                _logger.LogWarning(invalidByteRangeException.Message);
                var invalidByteRangeResponse = CreateResponse(HttpStatusCode.RequestedRangeNotSatisfiable, invalidByteRangeException);
                if (fileStream != null)
                {
                    fileStream.Close();
                }
                return Task.FromResult(invalidByteRangeResponse);
            }
            catch (Exception ex)
            {
                // Could not read the file
                _logger.LogError(ex, "Unable to read file: " + ex.Message);
                var errorResponse = CreateResponse(HttpStatusCode.NotFound, ex);
                if (fileStream != null)
                {
                    fileStream.Close();
                }
                return Task.FromResult(errorResponse);
            }
        }

        protected Task<HttpResponseMessage> CreateDirectoryPutResponse(HttpRequest request, DirectoryInfoBase info, string localFilePath)
        {
            if (info != null && info.Exists)
            {
                // Return a conflict result
                var conflictDirectoryResponse = CreateResponse(HttpStatusCode.Conflict);
                return Task.FromResult(conflictDirectoryResponse);
            }

            try
            {
                info.Create();
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, ex.Message);
                HttpResponseMessage conflictDirectoryResponse = CreateResponse(HttpStatusCode.Conflict);
                return Task.FromResult(conflictDirectoryResponse);
            }

            // Return 201 Created response
            var successFileResponse = CreateResponse(HttpStatusCode.Created);
            return Task.FromResult(successFileResponse);
        }

        protected async Task<HttpResponseMessage> CreateItemPutResponse(HttpRequest request, FileSystemInfoBase info, string localFilePath, bool itemExists)
        {
            // Check that we have a matching conditional If-Match request for existing resources
            if (itemExists)
            {
                // Get current etag
                var currentEtag = CreateEntityTag(info);
                var typedHeaders = request.GetTypedHeaders();
                // Existing resources require an etag to be updated.
                if (typedHeaders.IfMatch == null)
                {
                    var missingIfMatchResponse = CreateResponse(HttpStatusCode.PreconditionFailed, "missing If-Match");
                    return missingIfMatchResponse;
                }

                bool isMatch = false;
                foreach (var etag in typedHeaders.IfMatch)
                {
                    if (currentEtag.Equals(etag) || etag == Microsoft.Net.Http.Headers.EntityTagHeaderValue.Any)
                    {
                        isMatch = true;
                        break;
                    }
                }

                if (!isMatch)
                {
                    var conflictFileResponse = CreateResponse(HttpStatusCode.PreconditionFailed, "Etag mismatch");
                    conflictFileResponse.Headers.ETag = currentEtag.ToSystemETag();
                    return conflictFileResponse;
                }
            }

            // Save file
            try
            {
                using (Stream fileStream = GetFileWriteStream(localFilePath, fileExists: itemExists))
                {
                    try
                    {
                        await request.Body.CopyToAsync(fileStream);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, ex.Message);
                        var conflictResponse = CreateResponse(HttpStatusCode.Conflict, ex);
                        return conflictResponse;
                    }
                }

                // Return either 204 No Content or 201 Created response
                var successFileResponse = CreateResponse(itemExists ? HttpStatusCode.NoContent : HttpStatusCode.Created);

                // Set updated etag for the file
                info.Refresh();
                successFileResponse.SetEntityTagHeader(CreateEntityTag(info).ToSystemETag(), info.LastWriteTimeUtc);
                return successFileResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                var errorResponse = CreateResponse(HttpStatusCode.Conflict, ex);
                return errorResponse;
            }
        }

        protected Task<HttpResponseMessage> CreateFileDeleteResponse(HttpRequest request, FileInfoBase info)
        {
            // Existing resources require an etag to be updated.
            var typedHeaders = request.GetTypedHeaders();
            if (typedHeaders.IfMatch == null)
            {
                var conflictDirectoryResponse = CreateResponse(HttpStatusCode.PreconditionFailed, "Missing If-Match");
                return Task.FromResult(conflictDirectoryResponse);
            }

            // Get current etag
            var currentEtag = CreateEntityTag(info);
            var isMatch = typedHeaders.IfMatch.Any(etag => etag == Microsoft.Net.Http.Headers.EntityTagHeaderValue.Any || currentEtag.Equals(etag));

            if (!isMatch)
            {
                var conflictFileResponse = CreateResponse(HttpStatusCode.PreconditionFailed, "Etag mismatch");
                conflictFileResponse.Headers.ETag = currentEtag.ToSystemETag();
                return Task.FromResult(conflictFileResponse);
            }

            try
            {
                using (Stream fileStream = GetFileDeleteStream(info))
                {
                    info.Delete();
                }
                var successResponse = CreateResponse(HttpStatusCode.OK);
                return Task.FromResult(successResponse);
            }
            catch (Exception ex)
            {
                // Could not delete the file
                _logger.LogError(ex, ex.Message);
                var notFoundResponse = CreateResponse(HttpStatusCode.NotFound, ex);
                return Task.FromResult(notFoundResponse);
            }
        }

        /// <summary>
        /// Indicates whether this is a conditional range request containing an
        /// If-Range header with a matching etag and a Range header indicating the
        /// desired ranges
        /// </summary>
        protected bool IsRangeRequest(HttpRequest request, Net.Http.Headers.EntityTagHeaderValue currentEtag)
        {
            var typedHeaders = request.GetTypedHeaders();
            if (typedHeaders.Range == null)
            {
                return false;
            }
            if (typedHeaders.IfRange != null)
            {
                return typedHeaders.IfRange.EntityTag.Equals(currentEtag);
            }
            return true;
        }

        /// <summary>
        /// Indicates whether this is a If-None-Match request with a matching etag.
        /// </summary>
        protected bool IsIfNoneMatchRequest(HttpRequest request, EntityTagHeaderValue currentEtag)
        {
            var typedHeaders = request.GetTypedHeaders();
            return currentEtag != null && typedHeaders.IfNoneMatch != null &&
                typedHeaders.IfNoneMatch.Any(entityTag => currentEtag.Equals(entityTag));
        }

        /// <summary>
        /// Provides a common way for opening a file stream for shared reading from a file.
        /// </summary>
        protected static Stream GetFileReadStream(string localFilePath)
        {
            if (localFilePath == null)
            {
                throw new ArgumentNullException(nameof(localFilePath));
            }

            // Open file exclusively for read-sharing
            return FileUtility.Instance.File.Open(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        /// <summary>
        /// Provides a common way for opening a file stream for writing exclusively to a file.
        /// </summary>
        protected static Stream GetFileWriteStream(string localFilePath, bool fileExists)
        {
            if (localFilePath == null)
            {
                throw new ArgumentNullException(nameof(localFilePath));
            }

            // Create path if item doesn't already exist
            if (!fileExists)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localFilePath));
            }

            // Open file exclusively for write without any sharing
            return new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        }

        /// <summary>
        /// Provides a common way for opening a file stream for exclusively deleting the file.
        /// </summary>
        private static Stream GetFileDeleteStream(FileInfoBase file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            // Open file exclusively for delete sharing only
            return file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        /// <summary>
        /// Create unique etag based on the last modified UTC time
        /// </summary>
        private static Microsoft.Net.Http.Headers.EntityTagHeaderValue CreateEntityTag(FileSystemInfoBase sysInfo)
        {
            if (sysInfo == null)
            {
                throw new ArgumentNullException(nameof(sysInfo));
            }

            var etag = BitConverter.GetBytes(sysInfo.LastWriteTimeUtc.Ticks);

            var result = new StringBuilder(2 + (etag.Length * 2));
            result.Append("\"");
            foreach (byte b in etag)
            {
                result.AppendFormat("{0:x2}", b);
            }
            result.Append("\"");
            return new Microsoft.Net.Http.Headers.EntityTagHeaderValue(result.ToString());
        }

        // internal for testing purpose
        internal string GetLocalFilePath(HttpRequest request)
        {
            // Restore the original extension if we had added a dummy
            // See comment in TraceModule.OnBeginRequest
            string result = GetOriginalLocalFilePath(request);
            if (result.EndsWith(DummyRazorExtension, StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - DummyRazorExtension.Length);
            }

            return result;
        }

        private string GetOriginalLocalFilePath(HttpRequest request)
        {
            string result = null;
            PathString path = null;
            if (request.Path.StartsWithSegments("/admin/vfs", out path) ||
                request.Path.StartsWithSegments("/admin/zip", out path))
            {
                if (VfsSpecialFolders.TryParse(path, out result))
                {
                    return result;
                }
            }

            if (request.Query.TryGetValue("relativePath", out StringValues values) &&
                values.Contains("1"))
            {
                // the VFS path should be treated as relative to the functions script root directory
                result = Path.GetFullPath(_options.CurrentValue.ScriptPath);
            }
            else
            {
                result = RootPath;
            }

            if (path != null && path.HasValue)
            {
                result = Path.GetFullPath(Path.Combine(result, path.Value.TrimStart('/')));
            }
            else
            {
                string reqUri = request.GetRequestUri().AbsoluteUri.Split('?').First();
                if (reqUri[reqUri.Length - 1] == UriSegmentSeparator)
                {
                    result = Path.GetFullPath(result + Path.DirectorySeparatorChar);
                }
            }
            return result;
        }

        private IEnumerable<VfsStatEntry> GetDirectoryResponse(HttpRequest request, FileSystemInfoBase[] infos)
        {
            var uri = request.GetRequestUri();
            string baseAddress = uri.AbsoluteUri.Split('?').First();
            string query = uri.Query;
            foreach (FileSystemInfoBase fileSysInfo in infos)
            {
                bool isDirectory = (fileSysInfo.Attributes & FileAttributes.Directory) != 0;
                string mime = isDirectory ? _directoryMediaType.ToString() : MediaTypeMap.GetMediaType(fileSysInfo.Extension).ToString();
                string unescapedHref = isDirectory ? fileSysInfo.Name + UriSegmentSeparator : fileSysInfo.Name;
                long size = isDirectory ? 0 : ((FileInfoBase)fileSysInfo).Length;

                yield return new VfsStatEntry
                {
                    Name = fileSysInfo.Name,
                    MTime = fileSysInfo.LastWriteTimeUtc,
                    CRTime = fileSysInfo.CreationTimeUtc,
                    Mime = mime,
                    Size = size,
                    Href = (baseAddress + Uri.EscapeUriString(unescapedHref) + query).EscapeHashCharacter(),
                    Path = fileSysInfo.FullName
                };
            }

            // add special folders when requesting Root url
            // TODO: ahmels
            //var routeData = request.HttpContext.GetRouteData();
            //if (routeData != null && string.IsNullOrEmpty(routeData.Values["path"] as string))
            //{
            //    foreach (var entry in VfsSpecialFolders.GetEntries(baseAddress, query))
            //    {
            //        yield return entry;
            //    }
            //}
        }

        protected HttpResponseMessage CreateResponse(HttpStatusCode statusCode, object payload = null)
        {
            var response = new HttpResponseMessage(statusCode);
            if (payload != null)
            {
                var content = payload is string ? payload as string : JsonConvert.SerializeObject(payload);
                response.Content = new StringContent(content, Encoding.UTF8, "application/json");
            }
            return response;
        }
    }
}
