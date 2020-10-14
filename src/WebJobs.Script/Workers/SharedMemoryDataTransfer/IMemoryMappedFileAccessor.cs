// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO.MemoryMappedFiles;

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    public interface IMemoryMappedFileAccessor
    {
        /// <summary>
        /// Try to create a <see cref="MemoryMappedFile"/> with the specified name and size.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="size">Size of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> if created successfully,
        /// <see cref="null"/> otherwise.</param>
        /// <returns><see cref="true"/> if the <see cref="MemoryMappedFile"/> was created
        /// successfully, <see cref="false"/> otherwise.</returns>
        bool TryCreate(string mapName, long size, out MemoryMappedFile mmf);

        /// <summary>
        /// Try to open a <see cref="MemoryMappedFile"/>.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> to open.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> if opened successfully,
        /// <see cref="null"/> if not found.</param>
        /// <returns><see cref="true"/> if the <see cref="MemoryMappedFile"/> was successfully
        /// opened, <see cref="false"/> otherwise.</returns>
        bool TryOpen(string mapName, out MemoryMappedFile mmf);

        /// <summary>
        /// Delete a <see cref="MemoryMappedFile"/> and any underlying resources it uses.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> to delete.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to delete.</param>
        void Delete(string mapName, MemoryMappedFile mmf);

        /// <summary>
        /// Try to open (if already exists) a <see cref="MemoryMappedFile"/> with the
        /// specified name and size, or create a new one if no existing one is found.</summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="size">Size of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> if created or opened successfully,
        /// <see cref="null"/> otherwise.</param>
        /// <returns><see cref="true"/> if the <see cref="MemoryMappedFile"/> was created or opened
        /// successfully, <see cref="false"/> otherwise.</returns>
        bool TryCreateOrOpen(string mapName, long size, out MemoryMappedFile mmf);
    }
}
