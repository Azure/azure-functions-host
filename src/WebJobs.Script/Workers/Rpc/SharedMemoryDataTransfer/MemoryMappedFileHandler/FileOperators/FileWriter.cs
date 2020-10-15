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
    /// Class to perform content writing related operations on <see cref="MemoryMappedFile"/>.
    /// </summary>
    internal class FileWriter
    {
        private readonly ILogger _logger;

        private readonly FileAccessor _fileAccessor;

        private readonly FileReader _fileReader;

        public FileWriter(ILogger logger)
        {
            _logger = logger;
            _fileAccessor = new FileAccessor(_logger);
            _fileReader = new FileReader(_logger);
        }

        /// <summary>
        /// Create a new <see cref="MemoryMappedFile"/> with the given content.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to create.</param>
        /// <param name="content">Content to write into the newly created
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Response containing the newly created <see cref="MemoryMappedFile"/> if created
        /// successfully, failure response otherwise.</returns>
        public async Task<ResponseMemoryMappedFile> TryCreateWithContentAsync(
            string mapName,
            string content)
        {
            using (MemoryStream contentStream = new MemoryStream())
            using (StreamWriter writer = new StreamWriter(contentStream))
            {
                await writer.WriteAsync(content);
                await writer.FlushAsync();
                contentStream.Seek(0, SeekOrigin.Begin);

                return await TryCreateWithContentAsync(mapName, contentStream);
            }
        }

        /// <summary>
        /// Create a new <see cref="MemoryMappedFile"/> with the given content.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to create.</param>
        /// <param name="content">Content to write into the newly created
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Response containing the newly created <see cref="MemoryMappedFile"/> if created
        /// successfully, failure response otherwise.</returns>
        public async Task<ResponseMemoryMappedFile> TryCreateWithContentAsync(
            string mapName,
            byte[] content)
        {
            using (MemoryStream contentStream = new MemoryStream(content))
            {
                return await TryCreateWithContentAsync(mapName, contentStream);
            }
        }

        /// <summary>
        /// Create a new <see cref="MemoryMappedFile"/> with the given content.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to create.</param>
        /// <param name="content">Content to write into the newly created
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns>Response containing the newly created <see cref="MemoryMappedFile"/> if created
        /// successfully, failure response otherwise.</returns>
        public async Task<ResponseMemoryMappedFile> TryCreateWithContentAsync(
            string mapName,
            Stream content)
        {
            long contentLength = content.Length;
            byte[] contentLengthBytes = BitConverter.GetBytes(contentLength);
            long mmfLength = contentLength + contentLengthBytes.Length + 1;

            if (!_fileAccessor.TryCreate(mapName, mmfLength, out MemoryMappedFile mmf))
            {
                _logger.LogError($"Cannot create MemoryMappedFile: {mapName}");
                return ResponseMemoryMappedFile.FailureResponse;
            }

            try
            {
                using (MemoryMappedViewStream mmv = mmf.CreateViewStream())
                {
                    WriteFlag(mmv, MemoryMappedFileConstants.Content.ControlFlag.WriteInProgress);
                    await mmv.WriteAsync(contentLengthBytes, 0, MemoryMappedFileConstants.Content.LengthNumBytes);
                    await content.CopyToAsync(mmv, MemoryMappedFileConstants.Content.MinBufferSize);
                    WriteFlag(mmv, MemoryMappedFileConstants.Content.ControlFlag.ReadyToRead);
                    await mmv.FlushAsync();
                }

                // Do not close the MemoryMappedFile to make it available later
                return new ResponseMemoryMappedFile(true, mmf, mapName);
            }
            catch (Exception e)
            {
                // This exception can be triggered by:
                // 1) Size is greater than the total virtual memory.
                // 2) Access is invalid for the memory-mapped file.
                if (mmf != null)
                {
                    mmf.Dispose();
                }

                _logger.LogError(e, $"Cannot create MemoryMappedFile: {mapName}");
                return ResponseMemoryMappedFile.FailureResponse;
            }
        }

        public async Task<ResponseMemoryMappedFile> TryCopyContentAsync(
            MemoryMappedFile mmf)
        {
            // Get a Stream over the content contained in the MemoryMappedFile.
            var streamResponse = await _fileReader.TryGetContentStreamAsync(mmf);
            if (!streamResponse.Success)
            {
                return ResponseMemoryMappedFile.FailureResponse;
            }

            // Read the content from the content Stream of the MemoryMappedFile into a MemoryStream
            string mapName = Guid.NewGuid().ToString();
            return await TryCreateWithContentAsync(mapName, streamResponse.Value);
        }

        /// <summary>
        /// Create a new <see cref="MemoryMappedFile"/> with no content.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to create.</param>
        /// <param name="contentLength">Length to provision in the newly created
        /// <see cref="MemoryMappedFile"/> for adding content later.</param>
        /// <returns>Response containing the newly created <see cref="MemoryMappedFile"/> if created
        /// successfully, failure response otherwise.</returns>
        public async Task<ResponseMemoryMappedFile> TryCreateEmptyAsync(
            string mapName,
            long contentLength)
        {
            // Mark the MemoryMappedFile's content length as 0 (as it is empty initially)
            byte[] contentLengthBytes = BitConverter.GetBytes(0L);

            // But create it with a size equal to the contentLength (so it can be written to later)
            long mmfLength = contentLength + contentLengthBytes.Length + 1;

            if (!_fileAccessor.TryCreate(mapName, mmfLength, out MemoryMappedFile mmf))
            {
                _logger.LogError($"Cannot create MemoryMappedFile: {mapName}");
                return ResponseMemoryMappedFile.FailureResponse;
            }

            try
            {
                using (MemoryMappedViewStream mmv = mmf.CreateViewStream())
                {
                    WriteFlag(mmv, MemoryMappedFileConstants.Content.ControlFlag.WriteInProgress);
                    await mmv.WriteAsync(contentLengthBytes, 0, MemoryMappedFileConstants.Content.LengthNumBytes);
                    WriteFlag(mmv, MemoryMappedFileConstants.Content.ControlFlag.ReadyToRead);
                    await mmv.FlushAsync();
                }

                // Do not close the MemoryMappedFile to make it available later
                return new ResponseMemoryMappedFile(true, mmf, mapName);
            }
            catch (Exception e)
            {
                // This exception can be triggered by:
                // 1) Size is greater than the total virtual memory.
                // 2) Access is invalid for the memory-mapped file.
                if (mmf != null)
                {
                    mmf.Dispose();
                }

                _logger.LogError(e, $"Cannot create MemoryMappedFile: {mapName}");
                return ResponseMemoryMappedFile.FailureResponse;
            }
        }

        /// <summary>
        /// Overwrite the <see cref="MemoryMappedFile"/> with the given name with the content
        /// provided. The content must be at most the size with which the
        /// <see cref="MemoryMappedFile"/> was initially created.
        /// Note:
        /// If the new content that is overwriting some previous content is lesser in length,
        /// some remaining garbage data will remain in the <see cref="MemoryMappedFile"/>.
        /// However, since the header would now contain the new (shorter) content length, only
        /// the new content would be read as the <see cref="FileReader"/> will read content
        /// bytes equal to the content length specified in the header.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to create.</param>
        /// <param name="maxMapContentLength">Maximum length of content the
        /// <see cref="MemoryMappedFile"/> can contain.</param>
        /// <param name="content">Content to overwrite into the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns><see cref="true"/> if successfully overwritten, <see cref="false"/>
        /// otherwise.</returns>
        public async Task<bool> TryOverwriteContentAsync(
            string mapName,
            long maxMapContentLength,
            Stream content)
        {
            // Open the existing MemoryMappedFile to overwrite content into
            if (!_fileAccessor.TryOpen(mapName, out MemoryMappedFile mmf))
            {
                _logger.LogError($"Cannot open MemoryMappedFile: {mapName}");
                return false;
            }

            // Ensure that the MemoryMappedFile can contain the content being written
            long newContentLength = content.Length;
            if (newContentLength > maxMapContentLength)
            {
                _logger.LogError($"Cannot write content with length {newContentLength} in MemoryMappedFile ({mapName}) with max content length {maxMapContentLength}");
                return false;
            }

            byte[] newContentLengthBytes = BitConverter.GetBytes(newContentLength);
            using (MemoryMappedViewStream mmv = mmf.CreateViewStream())
            {
                // Change the flag to indicate that a write is in progress
                WriteFlag(mmv, MemoryMappedFileConstants.Content.ControlFlag.WriteInProgress);

                // Update the length of the content
                await mmv.WriteAsync(newContentLengthBytes, 0, MemoryMappedFileConstants.Content.LengthNumBytes);

                // Write the new content
                await content.CopyToAsync(mmv, MemoryMappedFileConstants.Content.MinBufferSize);

                // Change the flag to indicate that the write is complete
                WriteFlag(mmv, MemoryMappedFileConstants.Content.ControlFlag.ReadyToRead);

                await mmv.FlushAsync();
            }

            return true;
        }

        /// <summary>
        /// Overwrite the <see cref="MemoryMappedFile"/> with the given name with the content
        /// provided. The content must be at most the size with which the
        /// <see cref="MemoryMappedFile"/> was initially created.
        /// Note:
        /// If the new content that is overwriting some previous content is lesser in length,
        /// some remaining garbage data will remain in the <see cref="MemoryMappedFile"/>.
        /// However, since the header would now contain the new (shorter) content length, only
        /// the new content would be read as the <see cref="FileReader"/> will read content
        /// bytes equal to the content length specified in the header.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to create.</param>
        /// <param name="maxMapContentLength">Maximum length of content the
        /// <see cref="MemoryMappedFile"/> can contain.</param>
        /// <param name="content">Content to overwrite into the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns><see cref="true"/> if successfully overwritten, <see cref="false"/>
        /// otherwise.</returns>
        public async Task<bool> TryOverwriteContentAsync(
            string mapName,
            long maxMapContentLength,
            string content)
        {
            using (MemoryStream contentStream = new MemoryStream())
            using (StreamWriter writer = new StreamWriter(contentStream))
            {
                await writer.WriteAsync(content);
                await writer.FlushAsync();
                contentStream.Seek(0, SeekOrigin.Begin);
                return await TryOverwriteContentAsync(mapName, maxMapContentLength, contentStream);
            }
        }

        /// <summary>
        /// Append the <see cref="MemoryMappedFile"/> with the given name with the content
        /// provided. The total content after appending must be at most the size with which the
        /// <see cref="MemoryMappedFile"/> was initially created.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to create.</param>
        /// <param name="maxMapContentLength">Maximum length of content the
        /// <see cref="MemoryMappedFile"/> can contain.</param>
        /// <param name="content">Content to append at the end of the existing content in the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns><see cref="true"/> if successfully overwritten, <see cref="false"/>
        /// otherwise.</returns>
        public async Task<bool> TryAppendContentAsync(
            string mapName,
            long maxMapContentLength,
            Stream content)
        {
            if (!_fileAccessor.TryOpen(mapName, out MemoryMappedFile mmf))
            {
                _logger.LogError($"Cannot open MemoryMappedFile: {mapName}");
                return false;
            }

            var lengthResponse = await _fileReader.TryReadContentLengthAsync(mmf);
            if (!lengthResponse.Success)
            {
                _logger.LogError($"Cannot read content length for MemoryMappedFile: {mapName}");
                return false;
            }

            long currentContentLength = lengthResponse.Value;
            long newContentLength = currentContentLength + content.Length;
            if (newContentLength > maxMapContentLength)
            {
                _logger.LogError($"Cannot write content with length {newContentLength} in MemoryMappedFile ({mapName}) with max content length {maxMapContentLength}");
                return false;
            }

            byte[] newContentLengthBytes = BitConverter.GetBytes(newContentLength);
            long currentMmfLength = currentContentLength + newContentLengthBytes.Length + 1;
            using (MemoryMappedViewStream mmv = mmf.CreateViewStream())
            {
                // Change the flag to indicate that a write is in progress
                WriteFlag(mmv, MemoryMappedFileConstants.Content.ControlFlag.WriteInProgress);

                // Update the length of the content
                await mmv.WriteAsync(newContentLengthBytes, 0, MemoryMappedFileConstants.Content.LengthNumBytes);

                // Seek to the end of the existing content
                mmv.Seek(currentMmfLength, SeekOrigin.Begin);

                // Append the new content
                await content.CopyToAsync(mmv, MemoryMappedFileConstants.Content.MinBufferSize);

                // Change the flag to indicate that the write is complete
                WriteFlag(mmv, MemoryMappedFileConstants.Content.ControlFlag.ReadyToRead);

                await mmv.FlushAsync();
            }

            return true;
        }

        /// <summary>
        /// Append the <see cref="MemoryMappedFile"/> with the given name with the content
        /// provided. The total content after appending must be at most the size with which the
        /// <see cref="MemoryMappedFile"/> was initially created.
        /// </summary>
        /// <param name="mapName">Name of <see cref="MemoryMappedFile"/> to create.</param>
        /// <param name="fixedMapSize">Maximum length of content the
        /// <see cref="MemoryMappedFile"/> can contain.</param>
        /// <param name="content">Content to append at the end of the existing content in the
        /// <see cref="MemoryMappedFile"/>.</param>
        /// <returns><see cref="true"/> if successfully overwritten, <see cref="false"/>
        /// otherwise.</returns>
        public async Task<bool> TryAppendContentAsync(
            string mapName,
            long fixedMapSize,
            string content)
        {
            using (MemoryStream contentStream = new MemoryStream())
            using (StreamWriter writer = new StreamWriter(contentStream))
            {
                await writer.WriteAsync(content);
                await writer.FlushAsync();
                contentStream.Seek(0, SeekOrigin.Begin);
                return await TryAppendContentAsync(mapName, fixedMapSize, contentStream);
            }
        }

        /// <summary>
        /// Write the given flag to the <see cref="MemoryMappedFile"/> using its
        /// <see cref="MemoryMappedViewStream"/>.
        /// This will go to the beginning of the <see cref="MemoryMappedViewStream"/> to write.
        /// </summary>
        /// <param name="mmv"><see cref="MemoryMappedViewStream"/> over the content.</param>
        /// <param name="flag">Flag to assign.</param>
        private void WriteFlag(
            MemoryMappedViewStream mmv,
            MemoryMappedFileConstants.Content.ControlFlag flag)
        {
            mmv.Seek(0, SeekOrigin.Begin);
            mmv.WriteByte((byte)flag);
            mmv.Flush();
        }
    }
}
