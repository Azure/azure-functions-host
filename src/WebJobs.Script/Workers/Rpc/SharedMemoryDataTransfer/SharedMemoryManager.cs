// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO.MemoryMappedFiles;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal class SharedMemoryManager
    {
        /// <summary>
        /// Writes the given data into a <see cref="MemoryMappedFile"/>.
        /// </summary>
        /// <param name="data">Data to write into Shared Memory.</param>
        /// <returns>Name of the <see cref="MemoryMappedFile"/> into which data is written if
        /// successful, <see cref="null"/> otherwise.</returns>
        public async Task<string> TryPutAsync(byte[] data)
        {
            return await Task.FromResult("foo");
        }

        /// <summary>
        /// Reads data from the <see cref="MemoryMappedFile"/> with the given name.
        /// </summary>
        /// <param name="mmfName">Name of the <see cref="MemoryMappedFile"/> to read from.</param>
        /// <param name="offset">Offset to start reading data from in the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <param name="count">Number of bytes to read from, starting from the offset, in the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Data read as <see cref="byte[]"/> if successful, <see cref="null"/> otherwise.
        /// </returns>
        public byte[] TryGet(string mmfName, long offset, long count)
        {
            return new byte[1];
        }
    }
}
