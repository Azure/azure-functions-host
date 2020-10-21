// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    public class SharedMemoryManager : ISharedMemoryManager
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Mapping of invocation ID to list of names of shared memory maps allocated for that invocation.
        /// </summary>
        private readonly ConcurrentDictionary<string, IList<string>> _invocationSharedMemoryMaps;

        private readonly ConcurrentDictionary<string, SharedMemoryMap> _allocatedSharedMemoryMaps;

        public SharedMemoryManager(ILogger logger)
        {
            _logger = logger;
            _allocatedSharedMemoryMaps = new ConcurrentDictionary<string, SharedMemoryMap>();
            _invocationSharedMemoryMaps = new ConcurrentDictionary<string, IList<string>>();
        }

        public SharedMemoryManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("SharedMemoryManager");
            _allocatedSharedMemoryMaps = new ConcurrentDictionary<string, SharedMemoryMap>();
            _invocationSharedMemoryMaps = new ConcurrentDictionary<string, IList<string>>();
        }

        /// <summary>
        /// Writes the given data into a <see cref="MemoryMappedFile"/>.
        /// Note:
        /// Tracks the reference to the <see cref="SharedMemoryMap"/> after creating it and does not close it.
        /// </summary>
        /// <param name="content">Content to write into shared memory.</param>
        /// <returns>Name of the <see cref="MemoryMappedFile"/> into which data is written if
        /// successful, <see cref="null"/> otherwise.</returns>
        public async Task<SharedMemoryMetadata> PutBytesAsync(byte[] content)
        {
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryMap sharedMemoryMap = await SharedMemoryMap.CreateWithContentAsync(_logger, mapName, content);
            if (sharedMemoryMap != null)
            {
                if (_allocatedSharedMemoryMaps.TryAdd(mapName, sharedMemoryMap))
                {
                    SharedMemoryMetadata response = new SharedMemoryMetadata
                    {
                        Name = mapName,
                        Count = content.Length
                    };
                    return response;
                }
                else
                {
                    sharedMemoryMap.Dispose();
                }
            }

            _logger.LogError($"Cannot write content into MemoryMappedFile");
            return null;
        }

        public async Task<SharedMemoryMetadata> PutObjectAsync(object input)
        {
            if (input is byte[] arr)
            {
                return await PutBytesAsync(arr);
            }
            else
            {
                return null;
            }
        }

        public void TrackSharedMemoryMapForInvocation(string invocationId, string mapName)
        {
            IList<string> sharedMemoryMaps = _invocationSharedMemoryMaps.GetOrAdd(invocationId, new List<string>());
            sharedMemoryMaps.Add(mapName);
        }

        /// <summary>
        /// Reads data from the <see cref="MemoryMappedFile"/> with the given name.
        /// Note:
        /// Closes the reference to the <see cref="SharedMemoryMap"/> after reading.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> to read from.</param>
        /// <param name="offset">Offset to start reading data from in the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <param name="count">Number of bytes to read from, starting from the offset, in the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Data read as <see cref="byte[]"/> if successful, <see cref="null"/> otherwise.
        /// </returns>
        public async Task<byte[]> GetBytesAsync(string mapName, long offset, long count)
        {
            SharedMemoryMap sharedMemoryMap = SharedMemoryMap.Open(_logger, mapName);

            try
            {
                byte[] content = await sharedMemoryMap.TryReadAsBytesAsync(offset, count);

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
                sharedMemoryMap.DropReference();
            }
        }

        public bool TryFreeAllResourcesForInvocation(string invocationId)
        {
            bool freedAll = true;
            if (_invocationSharedMemoryMaps.TryRemove(invocationId, out IList<string> mapNames))
            {
                foreach (string mapName in mapNames)
                {
                    if (!TryFree(mapName))
                    {
                        freedAll = false;
                    }
                }
            }

            return freedAll;
        }

        public bool TryFree(string mapName)
        {
            if (_allocatedSharedMemoryMaps.TryRemove(mapName, out SharedMemoryMap sharedMemoryMap))
            {
                sharedMemoryMap.Dispose();
                return true;
            }
            else
            {
                _logger.LogWarning($"Cannot find SharedMemoryMap: {mapName}");
                return false;
            }
        }

        public bool IsSupported(object input)
        {
            // TODO gochaudh: add size check
            return IsDataTypeSupported(input);
        }

        private bool IsDataTypeSupported(object input)
        {
            if (input is byte[])
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
