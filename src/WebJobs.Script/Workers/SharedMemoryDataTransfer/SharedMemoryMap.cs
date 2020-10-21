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

        private readonly IMemoryMappedFileAccessor _mapAccessor;

        public SharedMemoryMap(ILogger logger, IMemoryMappedFileAccessor mapAccessor, string mapName, MemoryMappedFile mmf)
        {
            _logger = logger;
            _mapName = mapName;
            _mmf = mmf;
            _mapAccessor = mapAccessor;
        }

        /// <summary>
        /// Write content from the given <see cref="Stream"/> into this <see cref="SharedMemoryMap"/>.
        /// The number of bytes of content being written must be less than or equal to the size of the <see cref="SharedMemoryMap"/>.
        /// </summary>
        /// <param name="content"><see cref="Stream"/> containing the content to write.</param>
        /// <returns>Number of bytes of content written.</returns>
        private async Task<long> PutStreamAsync(Stream content)
        {
            long contentLength = content.Length;
            byte[] contentLengthBytes = BitConverter.GetBytes(contentLength);

            try
            {
                using (MemoryMappedViewStream mmv = _mmf.CreateViewStream())
                {
                    await mmv.WriteAsync(contentLengthBytes, 0, SharedMemoryConstants.LengthNumBytes);
                    await content.CopyToAsync(mmv, SharedMemoryConstants.MinBufferSize);
                    await mmv.FlushAsync();
                }

                return contentLength;
            }
            catch
            {
                // This exception can be triggered by:
                // 1) Size is greater than the total virtual memory.
                // 2) Access is invalid for the memory-mapped file.
                _mapAccessor.Delete(_mapName, _mmf);
            }

            _logger.LogError($"Cannot put bytes: {_mapName}");
            return 0;
        }

        /// <summary>
        /// Write content from the given <see cref="byte[]"/> into this <see cref="SharedMemoryMap"/>.
        /// The number of bytes of content being written must be less than or equal to the size of the <see cref="SharedMemoryMap"/>.
        /// </summary>
        /// <param name="content"><see cref="byte[]"/> containing the content to write.</param>
        /// <returns>Number of bytes of content written.</returns>
        public async Task<long> PutBytesAsync(byte[] content)
        {
            using (MemoryStream contentStream = new MemoryStream(content))
            {
                return await PutStreamAsync(contentStream);
            }
        }

        /// <summary>
        /// Read the content contained in this <see cref="SharedMemoryMap"/> as <see cref="byte[]"/>.
        /// </summary>
        /// <returns><see cref="byte[]"/> of content if successful, <see cref="null"/> otherwise.</returns>
        public async Task<byte[]> GetBytesAsync()
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
        public async Task<byte[]> GetBytesAsync(long offset, long count)
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
                using (MemoryMappedViewStream mmv = _mmf.CreateViewStream(0, SharedMemoryConstants.HeaderTotalBytes))
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
                    return _mmf.CreateViewStream(SharedMemoryConstants.HeaderTotalBytes + offset, count);
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
                return _mmf.CreateViewStream(SharedMemoryConstants.HeaderTotalBytes, contentLength);
            }

            return null;
        }

        public void Dispose()
        {
            _mapAccessor.Delete(_mapName, _mmf);
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
                long minSize = Math.Min(contentLength, SharedMemoryConstants.CopyBufferSize);
                int bufferSize = Convert.ToInt32(Math.Max(minSize, SharedMemoryConstants.MinBufferSize));
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
    }
}
