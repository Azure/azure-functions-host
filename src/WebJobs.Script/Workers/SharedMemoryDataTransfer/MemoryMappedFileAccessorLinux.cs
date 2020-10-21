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
        public MemoryMappedFileAccessorLinux(ILogger logger) : base(logger)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new Exception($"Cannot instantiate on this platform");
            }
        }

        public override bool TryCreate(string mapName, long size, out MemoryMappedFile mmf)
        {
            mmf = null;

            try
            {
                // Ensure the file is already not present
                string filePath = GetPath(mapName);
                if (filePath != null && File.Exists(filePath))
                {
                    Logger.LogError($"Cannot create MemoryMappedFile: {mapName}, file already exists");
                    return false;
                }

                // Get path of where to create the new file-based MemoryMappedFile
                filePath = CreatePath(mapName, size);
                if (string.IsNullOrEmpty(filePath))
                {
                    Logger.LogError($"Cannot create MemoryMappedFile: {mapName}, invalid file path");
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
                Logger.LogError(e, $"Cannot create MemoryMappedFile {mapName} for {size} bytes");
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
                    Logger.LogDebug($"Cannot open file (path {filePath} not found): {mapName}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Cannot open file: {mapName}");
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
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    string filePath = GetPath(mapName);
                    File.Delete(filePath);
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, $"Cannot delete MemoryMappedFile: {mapName}");
            }
        }
    }
}
