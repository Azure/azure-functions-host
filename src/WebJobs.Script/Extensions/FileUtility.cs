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
    }
}
