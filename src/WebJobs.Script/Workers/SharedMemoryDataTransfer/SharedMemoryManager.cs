// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    public class SharedMemoryManager
    {
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<string, SharedMemoryFile> _allocatedSharedMemoryFiles;

        public SharedMemoryManager(ILogger logger)
        {
            _logger = logger;
            _allocatedSharedMemoryFiles = new ConcurrentDictionary<string, SharedMemoryFile>();
        }

        /// <summary>
        /// Writes the given data into a <see cref="MemoryMappedFile"/>.
        /// Note:
        /// Tracks the reference to the <see cref="SharedMemoryFile"/> after creating it and does not close it.
        /// </summary>
        /// <param name="content">Content to write into Shared Memory.</param>
        /// <returns>Name of the <see cref="MemoryMappedFile"/> into which data is written if
        /// successful, <see cref="null"/> otherwise.</returns>
        public async Task<string> TryPutAsync(byte[] content)
        {
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryFile sharedMemoryFile = await SharedMemoryFile.CreateWithContentAsync(_logger, mapName, content);
            if (sharedMemoryFile != null)
            {
                if (_allocatedSharedMemoryFiles.TryAdd(mapName, sharedMemoryFile))
                {
                    return mapName;
                }
                else
                {
                    sharedMemoryFile.Dispose();
                }
            }

            _logger.LogError($"Cannot write content into MemoryMappedFile");
            return null;
        }

        /// <summary>
        /// Reads data from the <see cref="MemoryMappedFile"/> with the given name.
        /// Note:
        /// Closes the reference to the <see cref="SharedMemoryFile"/> after reading.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> to read from.</param>
        /// <param name="offset">Offset to start reading data from in the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <param name="count">Number of bytes to read from, starting from the offset, in the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Data read as <see cref="byte[]"/> if successful, <see cref="null"/> otherwise.
        /// </returns>
        public async Task<byte[]> TryGetAsync(string mapName, long offset, long count)
        {
            SharedMemoryFile sharedMemoryFile = SharedMemoryFile.Open(_logger, mapName);

            try
            {
                byte[] content = await sharedMemoryFile.TryReadAsBytesAsync(offset, count);

                if (content != null)
                {
                    return content;
                }
                else
                {
                    _logger.LogError($"Cannot read content from MemoryMappedFile: {mapName}");
                    return null;
                }
            }
            finally
            {
                sharedMemoryFile.DropReference();
            }
        }

        public bool TryFree(string mapName)
        {
            if (_allocatedSharedMemoryFiles.TryRemove(mapName, out SharedMemoryFile sharedMemoryFile))
            {
                sharedMemoryFile.Dispose();
                return true;
            }

            _logger.LogError($"Cannot free allocated MemoryMappedFile: {mapName}");
            return false;
        }
    }
}
