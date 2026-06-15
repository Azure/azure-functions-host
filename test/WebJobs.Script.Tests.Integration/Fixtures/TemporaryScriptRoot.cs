// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Fixtures
{
    /// <summary>
    /// Copies a source script root to a unique per-instance directory under
    /// <c>%TEMP%\{bucketName}\{yyMMdd-HHmmss}_{counter}</c> so that concurrent
    /// or sequential test fixtures do not race on shared on-disk artifacts
    /// (most notably the CSX <c>project.assets.json</c> file written back into
    /// the function directory by <c>PackageManager.RestorePackagesAsync</c>).
    /// </summary>
    /// <remarks>
    /// On <see cref="Dispose"/>, the most recent <paramref name="keepLastCopies"/>
    /// copies under the bucket are kept for post-failure inspection and the rest
    /// are best-effort deleted.
    /// </remarks>
    public sealed class TemporaryScriptRoot : IDisposable
    {
        private const int DefaultKeepLastCopies = 5;

        private readonly int _keepLastCopies;

        public TemporaryScriptRoot(string sourcePath, string bucketName, int keepLastCopies = DefaultKeepLastCopies)
        {
            ArgumentException.ThrowIfNullOrEmpty(sourcePath);
            ArgumentException.ThrowIfNullOrEmpty(bucketName);

            _keepLastCopies = keepLastCopies;

            string nowString = DateTime.UtcNow.ToString("yyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string GetDestPath(int counter) =>
                Path.Combine(Path.GetTempPath(), bucketName, $"{nowString}_{counter}");

            int i = 0;
            string destPath = GetDestPath(i++);
            while (Directory.Exists(destPath))
            {
                destPath = GetDestPath(i++);
            }

            FileUtility.CopyDirectory(sourcePath, destPath);
            RootPath = destPath;
        }

        public string RootPath { get; }

        public void Dispose()
        {
            string parent = System.IO.Path.GetDirectoryName(RootPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return;
            }

            try
            {
                var directoriesToDelete = Directory.EnumerateDirectories(parent)
                    .OrderByDescending(p => p, StringComparer.Ordinal)
                    .Skip(_keepLastCopies);

                foreach (string directory in directoriesToDelete)
                {
                    try
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                    catch
                    {
                        // best effort
                    }
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
