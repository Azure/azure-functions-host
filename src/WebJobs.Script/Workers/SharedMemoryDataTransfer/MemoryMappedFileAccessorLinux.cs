// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    public class MemoryMappedFileAccessorLinux : MemoryMappedFileAccessor
    {
        public MemoryMappedFileAccessorLinux(ILogger<MemoryMappedFileAccessor> logger) : base(logger)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new PlatformNotSupportedException("Cannot instantiate on this platform");
            }
        }

        public override bool TryCreate(string mapName, long size, out MemoryMappedFile mmf)
        {
            mmf = null;

            if (string.IsNullOrEmpty(mapName))
            {
                Logger.LogError("Cannot create MemoryMappedFile with invalid name");
                return false;
            }

            if (size <= 0)
            {
                Logger.LogError("Cannot create MemoryMappedFile: {MapName} with size: {Size} bytes", mapName, size);
                return false;
            }

            try
            {
                // Ensure the file is already not present
                string filePath = GetPath(mapName);
                if (filePath != null && File.Exists(filePath))
                {
                    Logger.LogError("Cannot create MemoryMappedFile: {MapName}, file already exists", mapName);
                    return false;
                }

                // Get path of where to create the new file-based MemoryMappedFile
                filePath = CreatePath(mapName, size);
                if (string.IsNullOrEmpty(filePath))
                {
                    Logger.LogError("Cannot create MemoryMappedFile: {MapName}, invalid file path", mapName);
                    return false;
                }

                mmf = MemoryMappedFile.CreateFromFile(
                    filePath,
                    FileMode.OpenOrCreate,
                    null, // Named maps are not supported on Linux
                    size,
                    MemoryMappedFileAccess.ReadWrite);
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Cannot create MemoryMappedFile: {mapName} with size: {Size} bytes", mapName, size);
            }

            return false;
        }

        public override bool TryOpen(string mapName, out MemoryMappedFile mmf)
        {
            mmf = null;

            try
            {
                string filePath = GetPath(mapName);
                if (filePath != null && File.Exists(filePath))
                {
                    mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open);
                    return true;
                }
                else
                {
                    Logger.LogDebug("Cannot open file path: {FilePath}, path not found", filePath);
                    return false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Cannot open MemoryMappedFile: {MapName}", mapName);
                return false;
            }
        }

        public override void Delete(string mapName, MemoryMappedFile mmf)
        {
            if (mmf != null)
            {
                mmf.Dispose();
            }

            try
            {
                string filePath = GetPath(mapName);
                File.Delete(filePath);
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, "Cannot delete MemoryMappedFile: {MapName}", mapName);
            }
        }

        /// <summary>
        /// Get the path of the file to store a <see cref="MemoryMappedFile"/>.
        /// We first try to mount it in memory-mounted directories (e.g. /dev/shm/).
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Path to the <see cref="MemoryMappedFile"/> if found an existing one
        /// <see cref="null"/> otherwise.</returns>
        public string GetPath(string mapName)
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
        public string CreatePath(string mapName, long size)
        {
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
        /// <see cref="SharedMemoryConstants.TempDirSuffix"/> is used.</returns>
        public string GetDirectory(long size)
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
                catch (Exception exception)
                {
                    Logger.LogWarning(exception, "Cannot get directory: {Directory}", tempDir);
                    continue;
                }
            }

            return null;
        }
    }
}