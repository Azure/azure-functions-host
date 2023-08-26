// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class ZipArchiveExtensions
    {
        public static async Task AddDirectory(this ZipArchive zipArchive, IDirectoryInfo directory, string directoryNameInArchive)
        {
            await InternalAddDirectory(zipArchive, directory, directoryNameInArchive);
        }

        private static async Task InternalAddDirectory(ZipArchive zipArchive, IDirectoryInfo directory, string directoryNameInArchive, IList<ZipArchiveEntry> files = null)
        {
            bool any = false;
            foreach (var info in directory.GetFileSystemInfos())
            {
                any = true;
                if (info is IDirectoryInfo subDirectoryInfo)
                {
                    string childName = ForwardSlashCombine(directoryNameInArchive, subDirectoryInfo.Name);
                    await InternalAddDirectory(zipArchive, subDirectoryInfo, childName, files);
                }
                else
                {
                    var entry = await zipArchive.AddFile((IFileInfo)info, directoryNameInArchive);
                    files?.Add(entry);
                }
            }

            if (!any)
            {
                // If the directory did not have any files or folders, add a entry for it
                zipArchive.CreateEntry(EnsureTrailingSlash(directoryNameInArchive));
            }
        }

        private static string ForwardSlashCombine(string part1, string part2)
        {
            return Path.Combine(part1, part2).Replace('\\', '/');
        }

        public static Task<ZipArchiveEntry> AddFile(this ZipArchive zipArchive, string filePath, string directoryNameInArchive = "")
        {
            var fileInfo = FileUtility.FileInfoFromFileName(filePath);
            return zipArchive.AddFile(fileInfo.Name, directoryNameInArchive);
        }

        public static async Task<ZipArchiveEntry> AddFile(this ZipArchive zipArchive, IFileInfo file, string directoryNameInArchive)
        {
            using (var fileStream = file.OpenRead())
            {
                string fileName = ForwardSlashCombine(directoryNameInArchive, file.Name);
                ZipArchiveEntry entry = zipArchive.CreateEntry(fileName, CompressionLevel.Fastest);
                entry.LastWriteTime = file.LastWriteTime;

                using (var zipStream = entry.Open())
                {
                    await fileStream.CopyToAsync(zipStream);
                }
                return entry;
            }
        }

        private static string EnsureTrailingSlash(string input)
        {
            return input.EndsWith("/", StringComparison.Ordinal) ? input : input + "/";
        }
    }
}
