// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    /// <summary>
    /// TODO gochaudh:
    /// Update docs. Param docs may have changed.
    /// </summary>
    public abstract class MemoryMappedFileAccessor : IMemoryMappedFileAccessor
    {
        public MemoryMappedFileAccessor(ILogger logger)
        {
            Logger = logger;
        }

        public MemoryMappedFileAccessor(ILoggerFactory loggerFactory)
        {
            Logger = loggerFactory.CreateLogger("MemoryMappedFileAccessor");
        }

        protected ILogger Logger { get; }

        public abstract bool TryCreate(string mapName, long size, out MemoryMappedFile mmf);

        public abstract bool TryOpen(string mapName, out MemoryMappedFile mmf);

        public abstract void Delete(string mapName, MemoryMappedFile mmf);

        public bool TryCreateOrOpen(string mapName, long size, out MemoryMappedFile mmf)
        {
            if (TryOpen(mapName, out mmf))
            {
                return true;
            }

            if (TryCreate(mapName, size, out mmf))
            {
                return true;
            }

            Logger.LogError($"Cannot create or open: {mapName} with size {size}");
            return false;
        }

        /// <summary>
        /// Get the path of the file to store a <see cref="MemoryMappedFile"/>.
        /// We first try to mount it in memory-mounted directories (e.g. /dev/shm/).
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Path to the <see cref="MemoryMappedFile"/> if found an existing one
        /// <see cref="null"/> otherwise.</returns>
        protected static string GetPath(string mapName)
        {
            // We escape the mapName to make it a valid file name
            // Python will use urllib.parse.quote_plus(mapName)
            string escapedMapName = Uri.EscapeDataString(mapName);

            // Check if the file already exists
            string filePath;
            foreach (string tempDir in SharedMemoryConstants.TempDirs)
            {
                filePath = Path.Combine(tempDir, SharedMemoryConstants.TempDirSuffix, escapedMapName);
                if (File.Exists(filePath))
                {
                    return filePath;
                }
            }

            return null;
        }

        /// <summary>
        /// Create a path which will be used to create a file backing a <see cref="MemoryMappedFile"/>.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="size">Required size in the directory.</param>
        /// <returns>Created path.</returns>
        protected static string CreatePath(string mapName, long size = 0)
        {
            // We escape the mapName to make it a valid file name
            // Python will use urllib.parse.quote_plus(mapName)
            string escapedMapName = Uri.EscapeDataString(mapName);

            // Create a new file
            string newTempDir = GetDirectory(size);
            if (newTempDir != null)
            {
                DirectoryInfo newTempDirInfo = Directory.CreateDirectory(newTempDir);
                return Path.Combine(newTempDirInfo.FullName, escapedMapName);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Get the path of the directory to create <see cref="MemoryMappedFile"/>.
        /// It checks which one has enough free space.
        /// </summary>
        /// <param name="size">Required size in the directory.</param>
        /// <returns>Directory with enough space to create <see cref="MemoryMappedFile"/>.
        /// If none of them has enough free space, the
        /// <see cref="MemoryMappedFileConstants.TempDirSuffix"/> is used.</returns>
        protected static string GetDirectory(long size = 0)
        {
            foreach (string tempDir in SharedMemoryConstants.TempDirs)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        DriveInfo driveInfo = new DriveInfo(tempDir);
                        long minSize = size + SharedMemoryConstants.TempDirMinSize;
                        if (driveInfo.AvailableFreeSpace > minSize)
                        {
                            return Path.Combine(tempDir, SharedMemoryConstants.TempDirSuffix);
                        }
                    }
                }
                catch (ArgumentException)
                {
                    continue;
                }
            }

            return null;
        }
    }
}
