// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Helpers
{
    public static class VfsSpecialFolders
    {
        private const string SystemDriveFolder = "SystemDrive";
        private const string LocalSiteRootFolder = "LocalSiteRoot";

        private static string _systemDrivePath;
        private static string _localSiteRootPath;

        public static string SystemDrivePath
        {
            get
            {
                // only return a system drive for Windows. Unix always assums / as fs root.
                if (_systemDrivePath == null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _systemDrivePath = Environment.GetEnvironmentVariable(SystemDriveFolder) ?? string.Empty;
                }

                return _systemDrivePath;
            }
        }

        public static string LocalSiteRootPath
        {
            get
            {
                if (_localSiteRootPath == null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // only light up in Azure env
                    string tmpPath = Environment.GetEnvironmentVariable("TMP");
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME")) &&
                        !string.IsNullOrEmpty(tmpPath))
                    {
                        _localSiteRootPath = Path.GetDirectoryName(tmpPath);
                    }
                }

                return _localSiteRootPath;
            }

            // internal for testing purpose
            internal set
            {
                _localSiteRootPath = value;
            }
        }

        public static IEnumerable<VfsStatEntry> GetEntries(string baseAddress, string query)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!string.IsNullOrEmpty(SystemDrivePath))
                {
                    var dir = FileUtility.DirectoryInfoFromDirectoryName(SystemDrivePath + Path.DirectorySeparatorChar);
                    yield return new VfsStatEntry
                    {
                        Name = SystemDriveFolder,
                        MTime = dir.LastWriteTimeUtc,
                        CRTime = dir.CreationTimeUtc,
                        Mime = "inode/shortcut",
                        Href = baseAddress + Uri.EscapeDataString(SystemDriveFolder + VirtualFileSystem.UriSegmentSeparator) + query,
                        Path = dir.FullName
                    };
                }

                if (!string.IsNullOrEmpty(LocalSiteRootPath))
                {
                    var dir = FileUtility.DirectoryInfoFromDirectoryName(LocalSiteRootPath);
                    yield return new VfsStatEntry
                    {
                        Name = LocalSiteRootFolder,
                        MTime = dir.LastWriteTimeUtc,
                        CRTime = dir.CreationTimeUtc,
                        Mime = "inode/shortcut",
                        Href = baseAddress + Uri.EscapeDataString(LocalSiteRootFolder + VirtualFileSystem.UriSegmentSeparator) + query,
                        Path = dir.FullName
                    };
                }
            }
        }

        public static bool TryHandleRequest(HttpRequest request, string path, out HttpResponseMessage response)
        {
            response = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                string.Equals(path, SystemDrivePath, StringComparison.OrdinalIgnoreCase))
            {
                response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                UriBuilder location = new UriBuilder(request.GetRequestUri());
                location.Path += "/";
                response.Headers.Location = location.Uri;
            }

            return response != null;
        }

        // this resolves the special folders such as SystemDrive or LocalSiteRoot
        public static bool TryParse(string path, out string result)
        {
            result = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !string.IsNullOrEmpty(path))
            {
                if (string.Equals(path, SystemDriveFolder, StringComparison.OrdinalIgnoreCase) ||
                    path.IndexOf(SystemDriveFolder + VirtualFileSystem.UriSegmentSeparator, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!string.IsNullOrEmpty(SystemDrivePath))
                    {
                        string relativePath = path.Substring(SystemDriveFolder.Length);
                        if (string.IsNullOrEmpty(relativePath))
                        {
                            result = SystemDrivePath;
                        }
                        else
                        {
                            result = Path.GetFullPath(SystemDrivePath + relativePath);
                        }
                    }
                }
                else if (string.Equals(path, LocalSiteRootFolder, StringComparison.OrdinalIgnoreCase) ||
                    path.IndexOf(LocalSiteRootFolder + VirtualFileSystem.UriSegmentSeparator, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    if (!string.IsNullOrEmpty(LocalSiteRootPath))
                    {
                        string relativePath = path.Substring(LocalSiteRootFolder.Length);
                        if (string.IsNullOrEmpty(relativePath))
                        {
                            result = LocalSiteRootPath;
                        }
                        else
                        {
                            result = Path.GetFullPath(LocalSiteRootPath + relativePath);
                        }
                    }
                }
            }

            return result != null;
        }
    }
}
