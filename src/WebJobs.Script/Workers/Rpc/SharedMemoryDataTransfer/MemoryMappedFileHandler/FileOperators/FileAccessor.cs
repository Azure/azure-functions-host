// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    /// <summary>
    /// Class to perform access operations related to <see cref="MemoryMappedFile"/> like creating
    /// new ones, opening existing ones, deleting etc.
    /// </summary>
    internal class FileAccessor
    {
        private readonly ILogger _logger;

        public FileAccessor(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Try to create a <see cref="MemoryMappedFile"/> with the specified name and size.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="size">Size of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> if created successfully,
        /// <see cref="null"/> otherwise.</param>
        /// <returns><see cref="true"/> if the <see cref="MemoryMappedFile"/> was created
        /// successfully, <see cref="false"/> otherwise.</returns>
        public bool TryCreate(
            string mapName,
            long size,
            out MemoryMappedFile mmf)
        {
            mmf = null;

            try
            {
                // Ensure the file is already not present
                string filePath = GetPath(mapName);
                if (filePath != null && File.Exists(filePath))
                {
                    _logger.LogError($"Cannot create MemoryMappedFile: {mapName}, file already exists");
                    return false;
                }

                // Get path of where to create the new file-based MemoryMappedFile
                filePath = CreatePath(mapName, size);
                if (string.IsNullOrEmpty(filePath))
                {
                    _logger.LogError($"Cannot create MemoryMappedFile: {mapName}, invalid file path");
                    return false;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    mmf = MemoryMappedFile.CreateFromFile(
                        filePath,
                        FileMode.OpenOrCreate,
                        mapName, // Named maps are supported on Windows
                        size,
                        MemoryMappedFileAccess.ReadWrite);
                    return true;
                }
                else
                {
                    mmf = MemoryMappedFile.CreateFromFile(
                        filePath,
                        FileMode.OpenOrCreate,
                        null, // Named maps are not supported on Linux
                        size,
                        MemoryMappedFileAccess.ReadWrite);
                    return true;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Cannot create MemoryMappedFile: {mapName} with size {size}");
                return false;
            }
        }

        /// <summary>
        /// Try to open a <see cref="MemoryMappedFile"/>.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> to open.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> if opened successfully,
        /// <see cref="null"/> if not found.</param>
        /// <returns><see cref="true"/> if the <see cref="MemoryMappedFile"/> was successfully
        /// opened, <see cref="false"/> otherwise.</returns>
        public bool TryOpen(
            string mapName,
            out MemoryMappedFile mmf)
        {
            mmf = null;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    mmf = MemoryMappedFile.OpenExisting(
                        mapName,
                        MemoryMappedFileRights.ReadWrite,
                        HandleInheritability.Inheritable);
                    return true;
                }
                else
                {
                    string filePath = GetPath(mapName);
                    if (filePath != null && File.Exists(filePath))
                    {
                        mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open);
                        return true;
                    }
                    else
                    {
                        _logger.LogDebug($"Cannot open file (path {filePath} not found): {mapName}");
                        return false;
                    }
                }
            }
            catch (FileNotFoundException fne)
            {
                _logger.LogDebug(fne, $"Cannot open file: {mapName}");
                return false;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Cannot open file: {mapName}");
                return false;
            }
        }

        /// <summary>
        /// Try to create or open (if already exists) a <see cref="MemoryMappedFile"/> with the
        /// specified name and size.</summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="size">Size of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> if created or opened successfully,
        /// <see cref="null"/> otherwise.</param>
        /// <returns><see cref="true"/> if the <see cref="MemoryMappedFile"/> was created or opened
        /// successfully, <see cref="false"/> otherwise.</returns>
        public bool TryCreateOrOpen(
            string mapName,
            long size,
            out MemoryMappedFile mmf)
        {
            mmf = null;

            if (TryOpen(mapName, out mmf))
            {
                return true;
            }

            if (TryCreate(mapName, size, out mmf))
            {
                return true;
            }

            _logger.LogError($"Cannot create or open: {mapName}, with size {size}");
            return false;
        }

        /// <summary>
        /// Check if a <see cref="MemoryMappedFile"/> with the given name exists.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to check.</param>
        /// <returns><see cref="true"/> if a <see cref="MemoryMappedFile"/> with the given name was
        /// found, <see cref="false"/> otherwise.</returns>
        public bool Exists(string mapName)
        {
            string filePath = GetPath(mapName);
            return filePath != null && File.Exists(filePath);
        }

        /// <summary>
        /// Dispose the given <see cref="MemoryMappedFile"/> and the corresponding file (if any)
        /// backing it.
        /// Note:
        /// The <see cref="MemoryMappedFile"/> being passed to delete must be the one created from
        /// <see cref="MemoryMappedFile.CreateFromFile"/> and not
        /// <see cref="MemoryMappedFile.OpenExisting"/>. Only the creator can delete the underlying
        /// resources.
        /// Reference: https://stackoverflow.com/a/17836555/3132415.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> to delete.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to delete and free resources from.</param>
        public void Delete(
            string mapName,
            MemoryMappedFile mmf)
        {
            if (mmf != null)
            {
                mmf.Dispose();
            }

            string filePath = GetPath(mapName);
            if (filePath != null && File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, $"Cannot delete MemoryMappedFile: {mapName}");
                }
            }
        }

        /// <summary>
        /// Dispose the given <see cref="MemoryMappedFile"/> with the given name and the
        /// corresponding file (if any) backing it.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> to delete.</param>
        public void Delete(string mapName)
        {
            if (TryOpen(mapName, out MemoryMappedFile mmf))
            {
                Delete(mapName, mmf);
            }
            else
            {
                // If the MemoryMappedFile does not exist, also make sure to clear any backing file
                // that may or may not exist.
                string filePath = GetPath(mapName);
                if (filePath != null && File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, $"Cannot delete MemoryMappedFile: {mapName}");
                    }
                }
            }
        }

        /// <summary>
        /// Get the path of the file to store a <see cref="MemoryMappedFile"/>.
        /// We first try to mount it in memory-mounted directories (e.g. /dev/shm/).
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Path to the <see cref="MemoryMappedFile"/> if found an existing one
        /// <see cref="null"/> otherwise.</returns>
        private string GetPath(string mapName)
        {
            // We escape the mapName to make it a valid file name
            // Python will use urllib.parse.quote_plus(mapName)
            string escapedMapName = Uri.EscapeDataString(mapName);

            // Check if the file already exists
            string filePath;
            foreach (string tempDir in MemoryMappedFileConstants.TempDirs)
            {
                filePath = Path.Combine(tempDir, MemoryMappedFileConstants.TempDirSuffix, escapedMapName);
                if (File.Exists(filePath))
                {
                    return filePath;
                }
            }

            string defaultTempDir = Path.GetTempPath();
            filePath = Path.Combine(defaultTempDir, MemoryMappedFileConstants.TempDirSuffix, escapedMapName);
            if (File.Exists(filePath))
            {
                return filePath;
            }

            return null;
        }

        /// <summary>
        /// Create a path which will be used to create a file backing a <see cref="MemoryMappedFile"/>.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="size">Required size in the directory.</param>
        /// <returns>Created path.</returns>
        private string CreatePath(string mapName, long size = 0)
        {
            // We escape the mapName to make it a valid file name
            // Python will use urllib.parse.quote_plus(mapName)
            string escapedMapName = Uri.EscapeDataString(mapName);

            // Create a new file
            string newTempDir = GetDirectory(size);
            DirectoryInfo newTempDirInfo = Directory.CreateDirectory(newTempDir);
            return Path.Combine(newTempDirInfo.FullName, escapedMapName);
        }

        /// <summary>
        /// Get the path of the directory to create <see cref="MemoryMappedFile"/>.
        /// It checks which one has enough free space.
        /// </summary>
        /// <param name="size">Required size in the directory.</param>
        /// <returns>Directory with enough space to create <see cref="MemoryMappedFile"/>.
        /// If none of them has enough free space, the
        /// <see cref="MemoryMappedFileConstants.TempDirSuffix"/> is used.</returns>
        private string GetDirectory(long size = 0)
        {
            foreach (string tempDir in MemoryMappedFileConstants.TempDirs)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        DriveInfo driveInfo = new DriveInfo(tempDir);
                        long minSize = size + MemoryMappedFileConstants.TempDirMinSize;
                        if (driveInfo.AvailableFreeSpace > minSize)
                        {
                            return Path.Combine(tempDir, MemoryMappedFileConstants.TempDirSuffix);
                        }
                    }
                }
                catch (ArgumentException)
                {
                    continue;
                }
            }

            string defaultTempDir = Path.GetTempPath();
            return Path.Combine(defaultTempDir, MemoryMappedFileConstants.TempDirSuffix);
        }
    }
}
