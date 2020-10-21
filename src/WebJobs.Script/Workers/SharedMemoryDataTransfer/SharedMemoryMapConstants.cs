// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    internal class SharedMemoryMapConstants
    {
        /// <summary>
        /// The length in number of bytes of a <see cref="long"/>.
        /// It is used to specify the length (in the header) of content contained in a <see cref="SharedMemoryMap"/>.
        /// </summary>
        public const int LengthNumBytes = sizeof(long);

        /// <summary>
        /// Length of the header in number of bytes at the start of a <see cref="SharedMemoryMap"/>.
        /// Note: Whenever the header is modified to contain more/less information, this needs to be updated.
        /// </summary>
        public const int HeaderTotalBytes = LengthNumBytes;

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
