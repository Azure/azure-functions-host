// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Bson;

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    /// <summary>
    /// Encapsulates functionality for accessing <see cref="MemoryMappedFile"/>.
    /// There are platform specific implementations of this:
    /// 1) <see cref="MemoryMappedFileAccessorWindows"/>
    /// 2) <see cref="MemoryMappedFileAccessorUnix"/>
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

        /// <summary>
        /// Check if the current OS platform is the same as the given valid platform.
        /// </summary>
        /// <param name="platform">Valid platform to check against.</param>
        protected void ValidatePlatform(OSPlatform platform)
        {
            if (!RuntimeInformation.IsOSPlatform(platform))
            {
                throw new PlatformNotSupportedException("Cannot instantiate on this platform");
            }
        }

        /// <summary>
        /// Check if the current OS platform is one of those given in the list of valid platforms.
        /// </summary>
        /// <param name="platforms">List of valid platforms to check against.</param>
        protected void ValidatePlatform(IList<OSPlatform> platforms)
        {
            if (!platforms.Any((platform) => RuntimeInformation.IsOSPlatform(platform)))
            {
                throw new PlatformNotSupportedException("Cannot instantiate on this platform");
            }
        }

        /// <summary>
        /// Write the given <see cref="bool"/> value into the <see cref="MemoryMappedViewStream"/>.
        /// </summary>
        /// <param name="mmv"><see cref="MemoryMappedViewStream"/> to write to.</param>
        /// <param name="value">The <see cref="bool"/> value to write.</param>
        private static void WriteBool(MemoryMappedViewStream mmv, bool value)
        {
            // The boolBytes array will be of length 1 so we pick the first byte
            byte[] boolBytes = BitConverter.GetBytes(value);
            byte boolByte = boolBytes[0];
            mmv.WriteByte(boolByte);
        }

        /// <summary>
        /// Read a <see cref="bool"/> value from the <see cref="MemoryMappedViewStream"/>.
        /// </summary>
        /// <param name="mmv"><see cref="MemoryMappedViewStream"/> to read from.</param>
        /// <returns>The read <see cref="bool"/> value.</returns>
        private static bool ReadBool(MemoryMappedViewStream mmv)
        {
            int boolInt = mmv.ReadByte();
            // ReadByte will return -1 if the byte could not be read
            if (boolInt == -1)
            {
                return false;
            }
            byte boolByte = (byte)boolInt;
            byte[] boolBytes = { boolByte };
            bool flag = BitConverter.ToBoolean(boolBytes, 0);
            return flag;
        }

        /// <summary>
        /// Checks if the <see cref="MemoryMappedFile"/> has been initialized or not.
        /// If it is set then it means the <see cref="MemoryMappedFile"/> was created and may already be in use.
        /// If it is not set then this <see cref="MemoryMappedFile"/> is new and can be used.
        /// </summary>
        /// <param name="mmf">The <see cref="MemoryMappedFile"/> to check the flag from.</param>
        /// <returns><see cref="true"/> if the <see cref="MemoryMappedFile"/> is initialized, <see cref="false"/> otherwise (the <see cref="MemoryMappedFile"/> is new).</returns>
        protected bool IsMemoryMapInitialized(MemoryMappedFile mmf)
        {
            using (MemoryMappedViewStream mmv = mmf.CreateViewStream(0, SharedMemoryConstants.MemoryMapInitializedHeaderBytes))
            {
                bool flag = ReadBool(mmv);
                if (flag == SharedMemoryConstants.MemoryMapInitializedFlag)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Sets the header flag in the <see cref="MemoryMappedFile"/> to indicate that this <see cref="MemoryMappedFile"/> is not new anymore and has been initialized.
        /// </summary>
        /// <param name="mmf">The <see cref="MemoryMappedFile"/> to set the flag on.</param>
        protected void SetMemoryMapInitialized(MemoryMappedFile mmf)
        {
            // Set the flag to indicate that this MemoryMappedFile is not new anymore before handing it out
            using (MemoryMappedViewStream mmv = mmf.CreateViewStream(0, SharedMemoryConstants.MemoryMapInitializedHeaderBytes))
            {
                WriteBool(mmv, SharedMemoryConstants.MemoryMapInitializedFlag);
            }
        }
    }
}