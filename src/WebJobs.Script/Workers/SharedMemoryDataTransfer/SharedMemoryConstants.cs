// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    internal class SharedMemoryConstants
    {
        /// <summary>
        /// The length in number of bytes of a <see cref="bool"/>.
        /// It is used to specify a flag if the <see cref="SharedMemoryMap"/> has been initialized or is new.
        /// </summary>
        public const int MemoryMapInitializedHeaderBytes = sizeof(bool);

        /// <summary>
        /// The length in number of bytes of a <see cref="long"/>.
        /// It is used to specify the length (in the header) of content contained in a <see cref="SharedMemoryMap"/>.
        /// </summary>
        public const int ContentLengthHeaderBytes = sizeof(long);

        /// <summary>
        /// A flag to indicate that the <see cref="SharedMemoryMap"/> has been allocated and initialized.
        /// </summary>
        public const bool MemoryMapInitializedFlag = true;

        /// <summary>
        /// Length of the header in number of bytes at the start of a <see cref="SharedMemoryMap"/>.
        /// Note: Whenever the header is modified to contain more/less information, this needs to be updated.
        /// </summary>
        public const int HeaderTotalBytes = MemoryMapInitializedHeaderBytes + ContentLengthHeaderBytes;

        /// <summary>
        /// Minimum size (in number of bytes) an object must be in order for it to be transferred over shared memory.
        /// If the object is smaller than this, gRPC is used.
        /// Note: This needs to be consistent among the host and workers.
        ///       e.g. in the Python worker, it is defined in shared_memory_constants.py
        /// </summary>
        public const long MinObjectBytesForSharedMemoryTransfer = 1024 * 1024; // 1 MB

        /// <summary>
        /// Maximum size (in number of bytes) an object can be in order for it to be transferred over shared memory.
        /// This limit is imposed because initializing objects like <see cref="byte[]"/> greater than 2GB is not allowed.
        /// Ref: https://stackoverflow.com/a/3944336/3132415
        /// Note: This needs to be consistent among the host and workers.
        ///       e.g. in the Python worker, it is defined in shared_memory_constants.py
        /// </summary>
        public const long MaxObjectBytesForSharedMemoryTransfer = ((long)2 * 1024 * 1024 * 1024) - 1; // 2 GB

        /// <summary>
        /// The minimum buffer size to copy from memory map 80 KB
        /// </summary>
        public const int MinBufferSize = 80 * 1024;

        /// <summary>
        /// Buffer size used when copying bytes to the memory map using a Stream.
        /// </summary>
        public const int CopyBufferSize = 64 * 1024 * 1024; // 64 MB

        /// <summary>
        /// Directory suffix to add to the temporary directory.
        /// </summary>
        public const string TempDirSuffix = "AzureFunctions";

        /// <summary>
        /// Minimum available size in a directory.
        /// By default, it needs at least 1 MB available.
        /// </summary>
        public const uint TempDirMinSize = 1024 * 1024;

        /// <summary>
        /// Directories for use with Shared Memory.
        /// These are needed in Linux where Shared Memory is backed by files.
        /// </summary>
        public static readonly List<string> TempDirs = new List<string>
        {
            "/dev/shm"
        };
    }
}
