// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class FileUtility
    {
        private static IFileSystem _default = new FileSystem();
        private static IFileSystem _instance;

        public static IFileSystem Instance
        {
            get { return _instance ?? _default; }
            set { _instance = value; }
        }

        public static void EnsureDirectoryExists(string path)
        {
            if (!Instance.Directory.Exists(path))
            {
                Instance.Directory.CreateDirectory(path);
            }
        }

        public static Task DeleteDirectoryAsync(string path, bool recursive)
        {
            return Task.Run(() =>
            {
                if (Instance.Directory.Exists(path))
                {
                    Instance.Directory.Delete(path, recursive);
                }
            });
        }

        public static Task<bool> DeleteIfExistsAsync(string path)
        {
            return Task.Run(() =>
            {
                if (Instance.File.Exists(path))
                {
                    Instance.File.Delete(path);
                    return true;
                }
                return false;
            });
        }

        public static async Task WriteAsync(string path, string contents, Encoding encoding = null)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (contents == null)
            {
                throw new ArgumentNullException(nameof(contents));
            }

            encoding = encoding ?? Encoding.UTF8;
            using (Stream fileStream = OpenFile(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            using (var writer = new StreamWriter(fileStream, encoding, 4096))
            {
                await writer.WriteAsync(contents);
            }
        }

        public static async Task<string> ReadAsync(string path, Encoding encoding = null)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            encoding = encoding ?? Encoding.UTF8;
            using (var fileStream = OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fileStream, encoding, true, 4096))
            {
                return await reader.ReadToEndAsync();
            }
        }

        public static string ReadAllText(string path) => Instance.File.ReadAllText(path);

        public static Stream OpenFile(string path, FileMode mode, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.None)
        {
            return Instance.File.Open(path, mode, access, share);
        }

        public static string GetRelativePath(string path1, string path2)
        {
            if (path1 == null)
            {
                throw new ArgumentNullException(nameof(path1));
            }

            if (path2 == null)
            {
                throw new ArgumentNullException(nameof(path2));
            }

            string EnsureTrailingSeparator(string path)
            {
                if (!Path.HasExtension(path) && !path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    path = path + Path.DirectorySeparatorChar;
                }

                return path;
            }

            path1 = EnsureTrailingSeparator(path1);
            path2 = EnsureTrailingSeparator(path2);

            var uri1 = new Uri(path1);
            var uri2 = new Uri(path2);

            Uri relativeUri = uri1.MakeRelativeUri(uri2);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString())
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            return relativePath;
        }

        public static Task<string[]> GetFilesAsync(string path, string prefix)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (prefix == null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }

            return Task.Run(() =>
            {
                return Instance.Directory.GetFiles(path, prefix);
            });
        }

        public static void CopyDirectory(string sourcePath, string targetPath)
        {
            foreach (string dirPath in Instance.Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Instance.Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            foreach (string filePath in Instance.Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                Instance.File.Copy(filePath, filePath.Replace(sourcePath, targetPath), true);
            }
        }

        public static bool FileExists(string path) => Instance.File.Exists(path);

        public static bool DirectoryExists(string path) => Instance.Directory.Exists(path);

        public static DirectoryInfoBase DirectoryInfoFromDirectoryName(string localSiteRootPath) => Instance.DirectoryInfo.FromDirectoryName(localSiteRootPath);

        public static FileInfoBase FileInfoFromFileName(string localFilePath) => Instance.FileInfo.FromFileName(localFilePath);

        public static string GetFullPath(string path) => Instance.Path.GetFullPath(path);

        private static void DeleteDirectoryContentsSafe(DirectoryInfoBase directoryInfo, bool ignoreErrors)
        {
            try
            {
                if (directoryInfo.Exists)
                {
                    foreach (var fsi in directoryInfo.GetFileSystemInfos())
                    {
                        DeleteFileSystemInfo(fsi, ignoreErrors);
                    }
                }
            }
            catch when (ignoreErrors)
            {
            }
        }

        private static void DeleteFileSystemInfo(FileSystemInfoBase fileSystemInfo, bool ignoreErrors)
        {
            if (!fileSystemInfo.Exists)
            {
                return;
            }

            try
            {
                fileSystemInfo.Attributes = FileAttributes.Normal;
            }
            catch when (ignoreErrors)
            {
            }

            if (fileSystemInfo is DirectoryInfoBase directoryInfo)
            {
                DeleteDirectoryContentsSafe(directoryInfo, ignoreErrors);
            }

            DoSafeAction(fileSystemInfo.Delete, ignoreErrors);
        }

        public static void DeleteDirectoryContentsSafe(string path, bool ignoreErrors = true)
        {
            try
            {
                var directoryInfo = DirectoryInfoFromDirectoryName(path);
                if (directoryInfo.Exists)
                {
                    foreach (var fsi in directoryInfo.GetFileSystemInfos())
                    {
                        DeleteFileSystemInfo(fsi, ignoreErrors);
                    }
                }
            }
            catch when (ignoreErrors)
            {
            }
        }

        public static void DeleteFileSafe(string path)
        {
            var info = FileInfoFromFileName(path);
            DeleteFileSystemInfo(info, ignoreErrors: true);
        }

        public static IEnumerable<string> EnumerateDirectories(string path) => Instance.Directory.EnumerateDirectories(path);

        private static void DoSafeAction(Action action, bool ignoreErrors)
        {
            try
            {
                action();
            }
            catch when (ignoreErrors)
            {
            }
        }
    }
}
