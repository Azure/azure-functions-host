// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class FileUtility
    {
        public static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        public static Task DeleteDirectoryAsync(string path, bool recursive)
        {
            return Task.Run(() =>
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive);
                }
            });
        }

        public static Task<bool> DeleteIfExistsAsync(string path)
        {
            return Task.Run(() =>
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
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
            using (var writer = new StreamWriter(path, false, encoding, 4096))
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
            using (var reader = new StreamReader(path, encoding, true, 4096))
            {
                return await reader.ReadToEndAsync();
            }
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
                return Directory.GetFiles(path, prefix);
            });
        }

        public static void CopyDirectory(string sourcePath, string targetPath)
        {
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            foreach (string filePath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(filePath, filePath.Replace(sourcePath, targetPath), true);
            }
        }
    }
}
