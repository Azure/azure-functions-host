// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.MemStore.MemoryMappedFileHandler.ResponseTypes;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal class SharedMemoryManager
    {
        private readonly ILogger _logger;

        private readonly FileAccessor _fileAccessor;

        private readonly FileReader _fileReader;

        private readonly FileWriter _fileWriter;

        private readonly ConcurrentDictionary<string, MemoryMappedFile> _allocatedMemoryMappedFiles;

        public SharedMemoryManager(ILogger logger)
        {
            _logger = logger;
            _fileAccessor = new FileAccessor(_logger);
            _fileReader = new FileReader(_logger);
            _fileWriter = new FileWriter(_logger);
            _allocatedMemoryMappedFiles = new ConcurrentDictionary<string, MemoryMappedFile>();
        }

        /// <summary>
        /// Writes the given data into a <see cref="MemoryMappedFile"/>.
        /// </summary>
        /// <param name="data">Data to write into Shared Memory.</param>
        /// <returns>Name of the <see cref="MemoryMappedFile"/> into which data is written if
        /// successful, <see cref="null"/> otherwise.</returns>
        public async Task<string> TryPutAsync(byte[] data)
        {
            string mapName = Guid.NewGuid().ToString();
            var createResponse = await _fileWriter.TryCreateWithContentAsync(mapName, data);
            if (createResponse.Success)
            {
                if (_allocatedMemoryMappedFiles.TryAdd(mapName, createResponse.Value))
                {
                    return mapName;
                }
                else
                {
                    _logger.LogError($"Unable to track allocated MemoryMappedFile: {mapName}");
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Reads data from the <see cref="MemoryMappedFile"/> with the given name.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> to read from.</param>
        /// <param name="offset">Offset to start reading data from in the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <param name="count">Number of bytes to read from, starting from the offset, in the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Data read as <see cref="byte[]"/> if successful, <see cref="null"/> otherwise.
        /// </returns>
        public byte[] TryGet(string mapName, long offset, long count)
        {
            return new byte[1];
        }

        public bool TryFree(string mapName)
        {
            if (_allocatedMemoryMappedFiles.TryRemove(mapName, out MemoryMappedFile mmf))
            {
                _fileAccessor.Delete(mapName, mmf);
                return true;
            }
            else
            {
                _logger.LogError($"Cannot free allocated MemoryMappedFile: {mapName}");
                return false;
            }
        }
    }
}
