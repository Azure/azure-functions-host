// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO.MemoryMappedFiles;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    /// <summary>
    /// Constants for the <see cref="MemoryMappedFile"/>.
    /// </summary>
    public static class MemoryMappedFileConstants
    {
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

        /// <summary>
        /// Constants for the memory map that manages content.
        /// </summary>
        public class Content
        {
            /// <summary>
            /// The length in number of bytes of a long.
            /// This is also in the Python side.
            /// Long telling total length of content contained in the memory map.
            /// Long supports files larger than 2GB.
            /// </summary>
            public const int LengthNumBytes = sizeof(long);

            /// <summary>
            /// Length of the header in number of bytes at the start of the memory map.
            /// </summary>
            public const int HeaderTotalBytes = sizeof(ControlFlag) + LengthNumBytes;

            /// <summary>
            /// The minimum buffer size to copy from memory map 80 KB
            /// </summary>
            public const int MinBufferSize = 80 * 1024;

            /// <summary>
            /// Buffer size used when copying bytes to the memory map using a Stream.
            /// </summary>
            public const int CopyBufferSize = 64 * 1024 * 1024; // 64 MB

            /// <summary>
            /// Control flag with the status of the content memory map.
            /// </summary>
            public enum ControlFlag : byte
            {
                /// <summary>
                /// Default state which is undefined.
                /// </summary>
                Unknown = 0,

                /// <summary>
                /// If the content memory map is ready to read.
                /// </summary>
                ReadyToRead = 1,

                /// <summary>
                /// If the content has already been read and can be removed.
                /// </summary>
                ReadyToDispose = 2,

                /// <summary>
                /// If we are still writing the contents.
                /// </summary>
                WriteInProgress = 3,

                /// <summary>
                /// The written content is pending to be read by the desired reader.
                /// </summary>
                PendingRead = 4,
            }
        }
    }
}
