// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO.MemoryMappedFiles;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    /// <summary>
    /// Encapsulates functionality for accessing <see cref="MemoryMappedFile"/>.
    /// There are platform specific implementations of this:
    /// 1) <see cref="MemoryMappedFileAccessorWindows"/>
    /// 2) <see cref="MemoryMappedFileAccessorLinux"/>
    /// </summary>
    public abstract class MemoryMappedFileAccessor : IMemoryMappedFileAccessor
    {
        public MemoryMappedFileAccessor(ILogger<MemoryMappedFileAccessor> logger)
        {
            Logger = logger;
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

            Logger.LogError("Cannot create or open shared memory map: {MapName} with size: {Size} bytes", mapName, size);
            return false;
        }
    }
}