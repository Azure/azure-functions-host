// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer
{
    /// <summary>
    /// TODO:
    /// 1) Add class description.
    /// 2) Logs to LogDebug or LogTrace.
    /// 3) Make sure all meaningful log places are logging.
    /// 4) long and int consistency - ensure > 2GB mmaps are handled.
    /// 5) put all private methods at the end.
    /// </summary>
    public class SharedMemoryMap : IDisposable
    {
        private readonly ILogger _logger;

        /// <summary>
        /// <see cref="MemoryMappedFile"/> containg the header + content of this <see cref="SharedMemoryMap"/>.
        /// </summary>
        private readonly MemoryMappedFile _mmf;

        /// <summary>
        /// Name of the <see cref="MemoryMappedFile"/> backing this <see cref="SharedMemoryMap"/>.
        /// </summary>
        private readonly string _mapName;

        public SharedMemoryMap(ILogger logger, string mapName, MemoryMappedFile mmf)
        {
            _logger = logger;
            _mapName = mapName;
            _mmf = mmf;
        }

        public static SharedMemoryMap Create(ILogger logger, string mapName, long size)
        {
            if (TryCreate(logger, mapName, size, out MemoryMappedFile mmf))
            {
                return new SharedMemoryMap(logger, mapName, mmf);
            }
            else
            {
                throw new Exception($"Cannot create MemoryMappedFile {mapName} with {size} bytes");
            }
        }

        public static SharedMemoryMap CreateOrOpen(ILogger logger, string mapName, long size)
        {
            if (TryCreateOrOpen(logger, mapName, size, out MemoryMappedFile mmf))
            {
                return new SharedMemoryMap(logger, mapName, mmf);
            }
            else
            {
                throw new Exception($"Cannot create or open MemoryMappedFile {mapName} with {size} bytes");
            }
        }

        public static SharedMemoryMap Open(ILogger logger, string mapName)
        {
            if (TryOpen(logger, mapName, out MemoryMappedFile mmf))
            {
                return new SharedMemoryMap(logger, mapName, mmf);
            }
            else
            {
                throw new Exception($"Cannot open MemoryMappedFile {mapName}");
            }
        }

        public static async Task<SharedMemoryMap> CreateWithContentAsync(ILogger logger, string mapName, byte[] content)
        {
            MemoryMappedFile mmf = await TryCreateWithContentAsync(logger, mapName, content);
            if (mmf != null)
            {
                return new SharedMemoryMap(logger, mapName, mmf);
            }
            else
            {
                throw new Exception($"Cannot create MemoryMappedFile {mapName} with {content.Length} bytes content");
            }
        }

        /// <summary>
        /// Try to create a <see cref="MemoryMappedFile"/> with the specified name and size.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="size">Size of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> if created successfully,
        /// <see cref="null"/> otherwise.</param>
        /// <returns><see cref="true"/> if the <see cref="MemoryMappedFile"/> was created
        /// successfully, <see cref="false"/> otherwise.</returns>
        private static bool TryCreate(ILogger logger, string mapName, long size, out MemoryMappedFile mmf)
        {
            mmf = null;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    mmf = MemoryMappedFile.CreateNew(
                        mapName, // Named maps are supported on Windows
                        size,
                        MemoryMappedFileAccess.ReadWrite);
                    return true;
                }
                else
                {
                    // Ensure the file is already not present
                    string filePath = GetPath(mapName);
                    if (filePath != null && File.Exists(filePath))
                    {
                        logger.LogError($"Cannot create MemoryMappedFile: {mapName}, file already exists");
                        return false;
                    }

                    // Get path of where to create the new file-based MemoryMappedFile
                    filePath = CreatePath(mapName, size);
                    if (string.IsNullOrEmpty(filePath))
                    {
                        logger.LogError($"Cannot create MemoryMappedFile: {mapName}, invalid file path");
                        return false;
                    }

                    mmf = MemoryMappedFile.CreateFromFile(
                        filePath,
                        FileMode.OpenOrCreate,
                        null, // Named maps are not supported on Linux
                        size,
                        MemoryMappedFileAccess.ReadWrite);
                    return true;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Cannot create MemoryMappedFile {mapName} for {size} bytes");
            }

            return false;
        }

        /// <summary>
        /// Try to open a <see cref="MemoryMappedFile"/>.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> to open.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> if opened successfully,
        /// <see cref="null"/> if not found.</param>
        /// <returns><see cref="true"/> if the <see cref="MemoryMappedFile"/> was successfully
        /// opened, <see cref="false"/> otherwise.</returns>
        private static bool TryOpen(ILogger logger, string mapName, out MemoryMappedFile mmf)
        {
            mmf = null;

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    mmf = MemoryMappedFile.OpenExisting(
                        mapName,
                        MemoryMappedFileRights.ReadWrite,
                        HandleInheritability.Inheritable);
                    return true;
                }
                else
                {
                    string filePath = GetPath(mapName);
                    if (filePath != null && File.Exists(filePath))
                    {
                        mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open);
                        return true;
                    }
                    else
                    {
                        logger.LogDebug($"Cannot open file (path {filePath} not found): {mapName}");
                        return false;
                    }
                }
            }
            catch (FileNotFoundException fne)
            {
                logger.LogDebug(fne, $"Cannot open file: {mapName}");
                return false;
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Cannot open file: {mapName}");
                return false;
            }
        }

        /// <summary>
        /// Try to open (if already exists) a <see cref="MemoryMappedFile"/> with the
        /// specified name and size, or create a new one if no existing one is found.</summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="size">Size of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> if created or opened successfully,
        /// <see cref="null"/> otherwise.</param>
        /// <returns><see cref="true"/> if the <see cref="MemoryMappedFile"/> was created or opened
        /// successfully, <see cref="false"/> otherwise.</returns>
        private static bool TryCreateOrOpen(ILogger logger, string mapName, long size, out MemoryMappedFile mmf)
        {
            if (TryOpen(logger, mapName, out mmf))
            {
                return true;
            }

            if (TryCreate(logger, mapName, size, out mmf))
            {
                return true;
            }

            logger.LogError($"Cannot create or open: {mapName} with size {size}");
            return false;
        }

        /// <summary>
        /// Create a new <see cref="MemoryMappedFile"/> with the given content.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to create.</param>
        /// <param name="content">Content to write into the newly created
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Response containing the newly created <see cref="MemoryMappedFile"/> if created
        /// successfully, failure response otherwise.</returns>
        private static async Task<MemoryMappedFile> TryCreateWithContentAsync(ILogger logger, string mapName, Stream content)
        {
            long contentLength = content.Length;
            byte[] contentLengthBytes = BitConverter.GetBytes(contentLength);
            long mmfLength = contentLength + contentLengthBytes.Length + 1;

            if (TryCreate(logger, mapName, mmfLength, out MemoryMappedFile mmf))
            {
                try
                {
                    using (MemoryMappedViewStream mmv = mmf.CreateViewStream())
                    {
                        await mmv.WriteAsync(contentLengthBytes, 0, SharedMemoryMapConstants.LengthNumBytes);
                        await content.CopyToAsync(mmv, SharedMemoryMapConstants.MinBufferSize);
                        await mmv.FlushAsync();
                    }

                    // Do not close the MemoryMappedFile to make it available later
                    return mmf;
                }
                catch
                {
                    // This exception can be triggered by:
                    // 1) Size is greater than the total virtual memory.
                    // 2) Access is invalid for the memory-mapped file.
                    Delete(logger, mapName, mmf);
                }
            }

            logger.LogError($"Cannot create MemoryMappedFile: {mapName}");
            return null;
        }

        /// <summary>
        /// Create a new <see cref="MemoryMappedFile"/> with the given content.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to create.</param>
        /// <param name="content">Content to write into the newly created
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Response containing the newly created <see cref="MemoryMappedFile"/> if created
        /// successfully, failure response otherwise.</returns>
        public static async Task<MemoryMappedFile> TryCreateWithContentAsync(ILogger logger, string mapName, byte[] content)
        {
            using (MemoryStream contentStream = new MemoryStream(content))
            {
                return await TryCreateWithContentAsync(logger, mapName, contentStream);
            }
        }

        /// <summary>
        /// Read the content contained in this <see cref="SharedMemoryMap"/> as <see cref="byte[]"/>.
        /// </summary>
        /// <returns><see cref="byte[]"/> of content if successful, <see cref="null"/> otherwise.</returns>
        public async Task<byte[]> TryReadAsBytesAsync()
        {
            using (MemoryStream contentStream = new MemoryStream())
            {
                if (await TryCopyToAsync(0, -1, contentStream))
                {
                    return contentStream.ToArray();
                }
            }

            return null;
        }

        /// <summary>
        /// Read the content contained in this <see cref="SharedMemoryMap"/> as <see cref="byte[]"/>.
        /// </summary>
        /// <param name="offset">Offset to start reading the content from.</param>
        /// <param name="count">Number of bytes to read.</param>
        /// <returns><see cref="byte[]"/> of content if successful, <see cref="null"/> otherwise.</returns>
        public async Task<byte[]> TryReadAsBytesAsync(long offset, long count)
        {
            using (MemoryStream contentStream = new MemoryStream())
            {
                if (await TryCopyToAsync(offset, count, contentStream))
                {
                    return contentStream.ToArray();
                }
            }

            return null;
        }

        /// <summary>
        /// Read the length of content present in this <see cref="SharedMemoryMap"/>.
        /// </summary>
        /// <returns><see cref="long"/> containing length of content if successful, -1 otherwise.</returns>
        public async Task<long> GetContentLengthAsync()
        {
            try
            {
                // Create a MemoryMappedViewStream over the header
                using (MemoryMappedViewStream mmv = _mmf.CreateViewStream(0, SharedMemoryMapConstants.HeaderTotalBytes))
                {
                    // Reads the content length (equal to the size of one long)
                    long contentLength = await ReadLongAsync(mmv);
                    if (contentLength >= 0)
                    {
                        return contentLength;
                    }
                    else
                    {
                        _logger.LogDebug($"Content length cannot be negative: {contentLength}");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, $"Cannot read content length for: {_mapName}");
            }

            return -1;
        }

        /// <summary>
        /// Get a <see cref="Stream"/> over the content stored in this <see cref="SharedMemoryMap"/> starting from
        /// the given offset until offset + count bytes.
        /// </summary>
        /// <returns><see cref="Stream"/> over the content if successful, <see cref="null"/> otherwise.</returns>
        public async Task<Stream> GetContentStreamAsync(int offset, int count)
        {
            if (offset < 0)
            {
                return null;
            }

            long contentLength = await GetContentLengthAsync();
            if (contentLength >= 0)
            {
                if (offset + count <= contentLength)
                {
                    return _mmf.CreateViewStream(SharedMemoryMapConstants.HeaderTotalBytes + offset, count);
                }
            }

            return null;
        }

        /// <summary>
        /// Get a <see cref="Stream"/> over the entire content stored in this <see cref="SharedMemoryMap"/>.
        /// </summary>
        /// <returns><see cref="Stream"/> over the content if successful, <see cref="null"/> otherwise.</returns>
        public async Task<Stream> GetContentStreamAsync()
        {
            long contentLength = await GetContentLengthAsync();
            if (contentLength >= 0)
            {
                return _mmf.CreateViewStream(SharedMemoryMapConstants.HeaderTotalBytes, contentLength);
            }

            return null;
        }

        /// <summary>
        /// Dispose the given <see cref="MemoryMappedFile"/> and the corresponding file (if any)
        /// backing it.
        /// Note:
        /// The <see cref="MemoryMappedFile"/> being passed to delete must be the one created from
        /// <see cref="MemoryMappedFile.CreateFromFile"/> and not
        /// <see cref="MemoryMappedFile.OpenExisting"/>. Only the creator can delete the underlying
        /// resources.
        /// Reference: https://stackoverflow.com/a/17836555/3132415.
        /// </summary>
        public static void Delete(ILogger logger, string mapName, MemoryMappedFile mmf)
        {
            if (mmf != null)
            {
                mmf.Dispose();
            }

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    string filePath = GetPath(mapName);
                    File.Delete(filePath);
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, $"Cannot delete MemoryMappedFile: {mapName}");
            }
        }

        public void Dispose()
        {
            Delete(_logger, _mapName, _mmf);
        }

        public void DropReference()
        {
            if (_mmf != null)
            {
                _mmf.Dispose();
            }
        }

        private async Task<long> ReadLongAsync(MemoryMappedViewStream mmv)
        {
            int numBytes = sizeof(long);
            byte[] buffer = new byte[numBytes];
            int numReadBytes = await mmv.ReadAsync(buffer, 0, numBytes);

            if (numReadBytes != numBytes)
            {
                throw new IOException($"Invalid header format. Expected {numBytes} bytes, found {numReadBytes} bytes.");
            }

            return BitConverter.ToInt64(buffer, 0);
        }

        /// <summary>
        /// Copy content from this <see cref="SharedMemoryMap"/> into a given <see cref="Stream"/>.
        /// </summary>
        /// <param name="offset">Offset to start reading the content from.</param>
        /// <param name="count">Number of bytes to read. -1 means read to completion.</param>
        /// <param name="destination"><see cref="Stream"/> to write content into.</param>
        /// <returns><see cref="true"/> if successfull, <see cref="false"/> otherwise.</returns>
        private async Task<bool> TryCopyToAsync(long offset, long count, Stream destination)
        {
            Stream contentStream = await GetContentStreamAsync();
            long contentLength = contentStream.Length;
            if (contentLength == 0)
            {
                return true;
            }

            if (offset > contentLength)
            {
                _logger.LogError($"Offset ({offset}) cannot be larger than content length ({contentLength})");
                return false;
            }

            contentStream.Seek(offset, SeekOrigin.Begin);
            long bytesToCopy = count == -1 ? contentStream.Length : count;

            using (contentStream)
            {
                // Explicitly provide a maximum buffer size
                long minSize = Math.Min(contentLength, SharedMemoryMapConstants.CopyBufferSize);
                int bufferSize = Convert.ToInt32(Math.Max(minSize, SharedMemoryMapConstants.MinBufferSize));
                if (!await CopyStreamAsync(contentStream, destination, (int)bytesToCopy, bufferSize))
                {
                    _logger.LogError($"Cannot copy {bytesToCopy} to destination Stream");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Copy given number of bytes from the input <see cref="Stream"/> to the output
        /// <see cref="Stream"/>.
        /// </summary>
        /// <param name="input">Input to read content from.</param>
        /// <param name="output">Output to write content into.</param>
        /// <param name="bytes">Number of bytes to copy from the input into the output.</param>
        /// <param name="bufferSize">Buffer size used while copying. Defaults to 32768 bytes (32KB).</param>
        /// <returns><see cref="true"/> if the required number of bytes were copied successfully,
        /// <see cref="false"/> otherwise.</returns>
        private async Task<bool> CopyStreamAsync(Stream input, Stream output, int bytes, int bufferSize = 32768)
        {
            long maxBytes = input.Length - input.Position;
            if (bytes > maxBytes)
            {
                _logger.LogError($"Cannot copy {bytes}; maximum {maxBytes} can be copied given the Stream length ({input.Length}) and current position ({input.Position})");
                return false;
            }

            byte[] buffer = new byte[bufferSize];
            int read;
            while (bytes > 0 && (read = input.Read(buffer, 0, Math.Min(buffer.Length, bytes))) > 0)
            {
                await output.WriteAsync(buffer, 0, read);
                bytes -= read;
            }

            return true;
        }

        /// <summary>
        /// Get the path of the file to store a <see cref="MemoryMappedFile"/>.
        /// We first try to mount it in memory-mounted directories (e.g. /dev/shm/).
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Path to the <see cref="MemoryMappedFile"/> if found an existing one
        /// <see cref="null"/> otherwise.</returns>
        private static string GetPath(string mapName)
        {
            // We escape the mapName to make it a valid file name
            // Python will use urllib.parse.quote_plus(mapName)
            string escapedMapName = Uri.EscapeDataString(mapName);

            // Check if the file already exists
            string filePath;
            foreach (string tempDir in SharedMemoryMapConstants.TempDirs)
            {
                filePath = Path.Combine(tempDir, SharedMemoryMapConstants.TempDirSuffix, escapedMapName);
                if (File.Exists(filePath))
                {
                    return filePath;
                }
            }

            return null;
        }

        /// <summary>
        /// Create a path which will be used to create a file backing a <see cref="MemoryMappedFile"/>.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/>.</param>
        /// <param name="size">Required size in the directory.</param>
        /// <returns>Created path.</returns>
        private static string CreatePath(string mapName, long size = 0)
        {
            // We escape the mapName to make it a valid file name
            // Python will use urllib.parse.quote_plus(mapName)
            string escapedMapName = Uri.EscapeDataString(mapName);

            // Create a new file
            string newTempDir = GetDirectory(size);
            if (newTempDir != null)
            {
                DirectoryInfo newTempDirInfo = Directory.CreateDirectory(newTempDir);
                return Path.Combine(newTempDirInfo.FullName, escapedMapName);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Get the path of the directory to create <see cref="MemoryMappedFile"/>.
        /// It checks which one has enough free space.
        /// </summary>
        /// <param name="size">Required size in the directory.</param>
        /// <returns>Directory with enough space to create <see cref="MemoryMappedFile"/>.
        /// If none of them has enough free space, the
        /// <see cref="MemoryMappedFileConstants.TempDirSuffix"/> is used.</returns>
        private static string GetDirectory(long size = 0)
        {
            foreach (string tempDir in SharedMemoryMapConstants.TempDirs)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        DriveInfo driveInfo = new DriveInfo(tempDir);
                        long minSize = size + SharedMemoryMapConstants.TempDirMinSize;
                        if (driveInfo.AvailableFreeSpace > minSize)
                        {
                            return Path.Combine(tempDir, SharedMemoryMapConstants.TempDirSuffix);
                        }
                    }
                }
                catch (ArgumentException)
                {
                    continue;
                }
            }

            return null;
        }
    }
}
