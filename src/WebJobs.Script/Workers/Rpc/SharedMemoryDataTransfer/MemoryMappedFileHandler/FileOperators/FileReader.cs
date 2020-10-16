// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.MemStore.MemoryMappedFileHandler.ResponseTypes;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    /// <summary>
    /// Class to read content (and related properties, like length, control flags etc.) from
    /// <see cref="MemoryMappedFile"/>.
    /// </summary>
    internal class FileReader
    {
        private readonly ILogger _logger;

        public FileReader(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Read the length of content present in a <see cref="MemoryMappedFile"/>.
        /// </summary>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to read the content length from.</param>
        /// <returns><see cref="ResponseLong"/> with read content length if successful, failure
        /// otherwise.</returns>
        public async Task<ResponseLong> TryReadContentLengthAsync(MemoryMappedFile mmf)
        {
            try
            {
                // Read the control data and check if the contents are ready to be read
                using (MemoryMappedViewStream mmv = mmf.CreateViewStream(0, MemoryMappedFileConstants.Content.HeaderTotalBytes))
                {
                    // Read the first byte (control flag) and increment the stream one step
                    if (!IsReadyToRead(mmv))
                    {
                        return ResponseLong.FailureResponse;
                    }

                    long contentLength = await ReadLongAsync(mmv);
                    if (contentLength < 0)
                    {
                        _logger.LogError($"Invalid content length: {contentLength}");
                        return ResponseLong.FailureResponse;
                    }

                    return new ResponseLong(true, contentLength);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Cannot read content length");
                return ResponseLong.FailureResponse;
            }
        }

        /// <summary>
        /// Read the content contained in the <see cref="MemoryMappedFile"/> as bytes.
        /// </summary>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to read the content from.</param>
        /// <param name="offset">Offset in the <see cref="MemoryMappedFile"/> to start
        /// reading the content from.</param>
        /// <param name="count">Number of bytes to read. -1 means read to completion.</param>
        /// <returns>Response containing a <see cref="byte[]"/> of content if successful, failure
        /// response otherwise.</returns>
        public async Task<ResponseByteArray> TryReadAsBytesAsync(MemoryMappedFile mmf, long offset, long count)
        {
            using (MemoryStream contentStream = new MemoryStream())
            {
                if (await TryCopyToAsync(mmf, offset, count, contentStream))
                {
                    return new ResponseByteArray(true, contentStream.ToArray());
                }
            }

            _logger.LogError("Cannot read bytes");
            return ResponseByteArray.FailureResponse;
        }

        /// <summary>
        /// Read the content contained in the <see cref="MemoryMappedFile"/> as bytes.
        /// </summary>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to read the content from.</param>
        /// <returns>Response containing a <see cref="byte[]"/> of content if successful, failure
        /// response otherwise.</returns>
        public async Task<ResponseByteArray> TryReadAsBytesAsync(MemoryMappedFile mmf)
        {
            return await TryReadAsBytesAsync(mmf, 0, -1);
        }

        /// <summary>
        /// Read the content contained in the <see cref="MemoryMappedFile"/> as a string.
        /// </summary>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to read the content from.</param>
        /// <param name="offset">Offset in the <see cref="MemoryMappedFile"/> to start
        /// reading the content from.</param>
        /// <param name="count">Number of bytes to read. -1 means read to completion.</param>
        /// <returns>Response containing a <see cref="string"/> of content if successful, failure
        /// response otherwise.</returns>
        public async Task<ResponseString> TryReadAsStringAsync(MemoryMappedFile mmf, long offset, long count)
        {
            using (MemoryStream contentStream = new MemoryStream())
            {
                if (await TryCopyToAsync(mmf, offset, count, contentStream))
                {
                    contentStream.Seek(0, SeekOrigin.Begin);
                    using (StreamReader streamReader = new StreamReader(contentStream))
                    {
                        string content = await streamReader.ReadToEndAsync();
                        return new ResponseString(true, content);
                    }
                }
            }

            _logger.LogError("Cannot read string");
            return ResponseString.FailureResponse;
        }

        /// <summary>
        /// Read the content contained in the <see cref="MemoryMappedFile"/> as a string.
        /// </summary>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to read the content from.</param>
        /// <returns>Response containing a <see cref="string"/> of content if successful, failure
        /// response otherwise.</returns>
        public async Task<ResponseString> TryReadAsStringAsync(MemoryMappedFile mmf)
        {
            return await TryReadAsStringAsync(mmf, 0, -1);
        }

        /// <summary>
        /// Copy all content from the <see cref="MemoryMappedFile"/> into a given
        /// <see cref="Stream"/>.
        /// </summary>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to read content from.</param>
        /// <param name="offset">Offset in the <see cref="MemoryMappedFile"/> to start
        /// reading the content from.</param>
        /// <param name="count">Number of bytes to read. -1 means read to completion.</param>
        /// <param name="stream"><see cref="Stream"/> to write content into.</param>
        /// <returns><see cref="true"/> if successfull, <see cref="false"/> otherwise.</returns>
        public async Task<bool> TryCopyToAsync(
            MemoryMappedFile mmf,
            long offset,
            long count,
            Stream stream)
        {
            // TODO Add unit tests with different values of offset/count

            var streamResponse = await TryGetContentStreamAsync(mmf);
            if (!streamResponse.Success)
            {
                _logger.LogError("Cannot get content stream");
                return false;
            }

            Stream contentStream = streamResponse.Value;
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
                long minSize = Math.Min(contentLength, MemoryMappedFileConstants.Content.CopyBufferSize);
                int bufferSize = Convert.ToInt32(Math.Max(minSize, MemoryMappedFileConstants.Content.MinBufferSize));
                if (!await CopyStreamAsync(contentStream, stream, (int)bytesToCopy, bufferSize))
                {
                    _logger.LogError($"Cannot copy {bytesToCopy} to destination Stream");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Copy all content from the <see cref="MemoryMappedFile"/> into a given
        /// <see cref="Stream"/>.
        /// </summary>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to read content from.</param>
        /// <param name="stream"><see cref="Stream"/> to write content into.</param>
        /// <returns><see cref="true"/> if successfull, <see cref="false"/> otherwise.</returns>
        public async Task<bool> TryCopyToAsync(
            MemoryMappedFile mmf,
            Stream stream)
        {
            return await TryCopyToAsync(mmf, 0, -1, stream);
        }

        /// <summary>
        /// Get a <see cref="Stream"/> over the entire content stored in the given
        /// <see cref="MemoryMappedFile"/>.
        /// </summary>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to read content from.</param>
        /// <returns>Response containing a <see cref="Stream"/> over the content if successful,
        /// failure response otherwise.</returns>
        public async Task<ResponseStream> TryGetContentStreamAsync(MemoryMappedFile mmf)
        {
            // TODO: Add unit tests.
            var lengthResponse = await TryReadContentLengthAsync(mmf);
            if (!lengthResponse.Success)
            {
                _logger.LogError("Cannot read content length");
                return ResponseStream.FailureResponse;
            }

            long contentLength = lengthResponse.Value;
            if (contentLength == 0)
            {
                return new ResponseStream(true, new MemoryStream());
            }

            return new ResponseStream(true, mmf.CreateViewStream(MemoryMappedFileConstants.Content.HeaderTotalBytes, contentLength));
        }

        /// <summary>
        /// Get a <see cref="Stream"/> over the content stored in the given
        /// <see cref="MemoryMappedFile"/> starting from the given offset and containing the given
        /// number of bytes.
        /// </summary>
        /// <param name="mmf"><see cref="MemoryMappedFile"/> to read content from.</param>
        /// <returns>Response containing a <see cref="Stream"/> over the content if successful,
        /// failure response otherwise.</returns>
        public async Task<ResponseStream> TryGetContentStreamAsync(MemoryMappedFile mmf, int contentOffset, int contentLengthToRead)
        {
            // TODO: Add unit tests.
            if (contentOffset < 0)
            {
                _logger.LogError($"Invalid content offset: {contentOffset}");
                return ResponseStream.FailureResponse;
            }

            var lengthResponse = await TryReadContentLengthAsync(mmf);
            if (!lengthResponse.Success)
            {
                _logger.LogError("Cannot read content length");
                return ResponseStream.FailureResponse;
            }

            long contentLength = lengthResponse.Value;
            if (contentLength == 0)
            {
                return new ResponseStream(true, new MemoryStream());
            }

            if (contentOffset + contentLengthToRead > contentLength)
            {
                _logger.LogError($"Content offset ({contentOffset}) + count of bytes to read ({contentLengthToRead}) cannot be more than total content length ({contentLength})");
                return ResponseStream.FailureResponse;
            }

            return new ResponseStream(true, mmf.CreateViewStream(MemoryMappedFileConstants.Content.HeaderTotalBytes + contentOffset, contentLengthToRead));
        }

        /// <summary>
        /// Read the control byte of the <see cref="MemoryMappedFile"/> using its
        /// <see cref="MemoryMappedViewStream"/>.
        /// This will go to the beginning of the <see cref="MemoryMappedViewStream"/> to read from.
        /// </summary>
        /// <param name="mmv"><see cref="MemoryMappedViewStream"/> to read control flag from.</param>
        /// <returns>Control flag.</returns>
        private int ReadControlFlag(MemoryMappedViewStream mmv)
        {
            mmv.Seek(0, SeekOrigin.Begin);
            int controlData = mmv.ReadByte();
            return controlData;
        }

        /// <summary>
        /// Read one long from the <see cref="MemoryMappedViewStream"/>.
        /// </summary>
        /// <param name="mmv">Input <see cref="MemoryMappedViewStream"/> containing data to read
        /// from.</param>
        /// <returns>Long value read from the <see cref="MemoryMappedViewStream"/>.</returns>
        private async Task<long> ReadLongAsync(MemoryMappedViewStream mmv)
        {
            int numBytes = sizeof(long);
            byte[] buffer = new byte[numBytes];
            int numReadBytes = await mmv.ReadAsync(buffer, 0, numBytes);

            if (numReadBytes != numBytes)
            {
                throw new IOException($"Wrong header for the mmv length: {numReadBytes} != {numBytes}");
            }

            return BitConverter.ToInt64(buffer, 0);
        }

        public bool IsPendingRead(MemoryMappedViewStream mmv)
        {
            int readyToReadFlag = (int)MemoryMappedFileConstants.Content.ControlFlag.PendingRead;
            return IsControlFlag(mmv, readyToReadFlag);
        }

        /// <summary>
        /// Check if the read control flag is equal to the expected control flag.
        /// </summary>
        /// <param name="mmv">Input <see cref="MemoryMappedViewStream"/> containing data to read
        /// from.</param>
        /// <param name="expectedControlFlag">Control flag to match against.</param>
        /// <returns><see cref="true"/> if the read control flag matches the expected control flag.
        /// </returns>
        private bool IsControlFlag(MemoryMappedViewStream mmv, int expectedControlFlag)
        {
            int controlFlag = ReadControlFlag(mmv);
            return controlFlag == expectedControlFlag;
        }

        /// <summary>
        /// Check if the <see cref="MemoryMappedFile"/> is ready to be read.
        /// </summary>
        /// <param name="mmv"><see cref="MemoryMappedViewStream"/> for the
        /// <see cref="MemoryMappedFile"/> to check status of.</param>
        /// <returns><see cref="true"/> if the content is ready to read, <see cref="false"/>
        /// otherwise.</returns>
        private bool IsReadyToRead(MemoryMappedViewStream mmv)
        {
            int readyToReadFlag = (int)MemoryMappedFileConstants.Content.ControlFlag.ReadyToRead;
            return IsControlFlag(mmv, readyToReadFlag);
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
        public async Task<bool> CopyStreamAsync(Stream input, Stream output, int bytes, int bufferSize = 32768)
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
