// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    /// <summary>
    /// Tests for <see cref="SharedMemoryMap"/>.
    /// </summary>
    public class SharedMemoryMapTests
    {
        private readonly ILoggerFactory _loggerFactory;

        private readonly IMemoryMappedFileAccessor _mapAccessor;

        private readonly IEnvironment _testEnvironment;

        public SharedMemoryMapTests()
        {
            _loggerFactory = MockNullLoggerFactory.CreateLoggerFactory();

            ILogger<MemoryMappedFileAccessor> logger = NullLogger<MemoryMappedFileAccessor>.Instance;

            _testEnvironment = new TestEnvironment();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _mapAccessor = new MemoryMappedFileAccessorWindows(logger);
            }
            else
            {
                _mapAccessor = new MemoryMappedFileAccessorUnix(logger, _testEnvironment);
            }
        }

        /// <summary>
        /// Create a <see cref="SharedMemoryMap"/> with valid inputs.
        /// Verify that it is created.
        /// </summary>
        /// <param name="contentSize">Size of content to put inside the <see cref="SharedMemoryMap"/>.</param>
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1024)]
        [Theory]
        public void Create_VerifyCreated(long contentSize)
        {
            string mapName = Guid.NewGuid().ToString();
            long size = contentSize + SharedMemoryConstants.HeaderTotalBytes;
            Assert.True(_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf));

            SharedMemoryMap sharedMemoryMap = new SharedMemoryMap(_loggerFactory, _mapAccessor, mapName, mmf);
            Assert.NotNull(sharedMemoryMap);

            sharedMemoryMap.Dispose();
        }

        /// <summary>
        /// Create a <see cref="SharedMemoryMap"/> with invalid <see cref="MemoryMappedFile"/>.
        /// Verify that it is not created and throws an exception
        /// </summary>
        [Fact]
        public void Create_InvalidMemorMappedFile_VerifyThrowsException()
        {
            string mapName = Guid.NewGuid().ToString();

            Assert.Throws<ArgumentNullException>(() => new SharedMemoryMap(_loggerFactory, _mapAccessor, mapName, null));
        }

        /// <summary>
        /// Create a <see cref="SharedMemoryMap"/> with invalid map name.
        /// Verify that it is not created and throws an exception
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> backing the <see cref="SharedMemoryMap"/>.</param>
        [InlineData(null)]
        [InlineData("")]
        [Theory]
        public void Create_InvalidMapName_VerifyThrowsException(string mapName)
        {
            long contentSize = 1024;
            long size = contentSize + SharedMemoryConstants.HeaderTotalBytes;
            MemoryMappedFile mmf = MemoryMappedFile.CreateNew("foo", contentSize);

            Assert.Throws<ArgumentException>(() => new SharedMemoryMap(_loggerFactory, _mapAccessor, mapName, mmf));
            _mapAccessor.Delete("foo", mmf);
        }

        /// <summary>
        /// Create a <see cref="SharedMemoryMap"/> and put some content in it using <see cref="byte[]"/>.
        /// Since <see cref="byte[]"/> cannot be more than 2GB, that's the maximum we will try to put.
        /// Verify that the content is written.
        /// </summary>
        /// <param name="contentSize">Size of content to put inside the <see cref="SharedMemoryMap"/>.</param>
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1024)] // 1KB
        [InlineData(1024 * 1024)] // 1MB
        [InlineData(256 * 1024 * 1024)] // 256MB
        [InlineData(1024 * 1024 * 1024, Skip = "Results in OOM when run on test infra")] // 1GB
        [Theory]
        public async Task PutBytes_VerifySuccess(int contentSize)
        {
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryMap sharedMemoryMap = Create(mapName, contentSize);
            byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

            long bytesWritten = await sharedMemoryMap.PutBytesAsync(content);
            Assert.Equal(contentSize, bytesWritten);

            sharedMemoryMap.Dispose();
        }

        /// <summary>
        /// Create a <see cref="SharedMemoryMap"/> and put some content in it using <see cref="UnmanagedMemoryStream"/>.
        /// Since <see cref="UnmanagedMemoryStream"/> can hold more than 2GB data, we try it with larger inputs.
        /// Verify that the content is written.
        /// </summary>
        /// <param name="contentSize">Size of content to put inside the <see cref="SharedMemoryMap"/>.</param>
        [InlineData((long)2 * 1024 * 1024 * 1024)] // 2GB
        [InlineData((long)3 * 1024 * 1024 * 1024)] // 3GB
        [Theory(Skip = "Results in OOM when run on test infra")]
        public async Task PutStream_LargeData_VerifySuccess(long contentSize)
        {
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryMap sharedMemoryMap = Create(mapName, contentSize);

            // Since the size of memory being requested can be greater than 2GB, a 32 bit
            // integer type can't hold that. So we use an IntPtr to store the size, which
            // on a 64 bit platform would store the size in a 64 bit integer. Then we can pass
            // a pointer to that integer to the memory allocation method.
            IntPtr sizePtr = new IntPtr(contentSize);

            // Allocate a block of unmanaged memory
            IntPtr memIntPtr = Marshal.AllocHGlobal(sizePtr);

            // Generate content to put into the shared memory map
            Stream content = TestUtils.GetRandomContentInStream(contentSize, memIntPtr);

            // Put content into the shared memory map
            long bytesWritten = await sharedMemoryMap.PutStreamAsync(content);
            Assert.Equal(contentSize, bytesWritten);

            // Free the block of unmanaged memory
            Marshal.FreeHGlobal(memIntPtr);

            content.Close();
            content.Dispose();

            sharedMemoryMap.Dispose();
        }

        /// <summary>
        /// Create a <see cref="SharedMemoryMap"/> and put some content in it using <see cref="byte[]"/> and try to read it back.
        /// Since <see cref="byte[]"/> cannot be more than 2GB, that's the maximum we will try to put.
        /// Verify that the content read matches what was written.
        /// </summary>
        /// <param name="contentSize">Size of content to put inside the <see cref="SharedMemoryMap"/>.</param>
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1024)] // 1KB
        [InlineData(1024 * 1024)] // 1MB
        [InlineData(256 * 1024 * 1024)] // 256MB
        [InlineData(1024 * 1024 * 1024, Skip = "Results in OOM when run on test infra")] // 1GB
        [Theory]
        public async Task GetBytes_VerifyContentMatches(int contentSize)
        {
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryMap sharedMemoryMap = Create(mapName, contentSize);
            byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

            await sharedMemoryMap.PutBytesAsync(content);

            byte[] readContent = await sharedMemoryMap.GetBytesAsync();
            Assert.True(TestUtils.UnsafeCompare(content, readContent));

            sharedMemoryMap.Dispose();
        }

        /// <summary>
        /// Create a <see cref="SharedMemoryMap"/> and put some content in it using <see cref="byte[]"/> and try to read it back
        /// as a <see cref="Stream"/>.
        /// Since <see cref="byte[]"/> cannot be more than 2GB, that's the maximum we will try to put.
        /// Verify that the content read matches what was written.
        /// </summary>
        /// <param name="contentSize">Size of content to put inside the <see cref="SharedMemoryMap"/>.</param>
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1024)] // 1KB
        [InlineData(1024 * 1024)] // 1MB
        [InlineData(256 * 1024 * 1024)] // 256MB
        [InlineData(1024 * 1024 * 1024, Skip = "Results in OOM when run on test infra")] // 1GB
        [Theory]
        public async Task GetStream_VerifyContentMatches(int contentSize)
        {
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryMap sharedMemoryMap = Create(mapName, contentSize);
            byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

            long bytesWritten = await sharedMemoryMap.PutBytesAsync(content);
            Assert.Equal(contentSize, bytesWritten);

            Stream readContent = await sharedMemoryMap.GetStreamAsync();

            static IEnumerable<byte> StreamToEnumerable(Stream stream)
            {
                for (int i = stream.ReadByte(); i != -1; i = stream.ReadByte())
                {
                    yield return (byte)i;
                }
            }

            var readContentEnumerable = StreamToEnumerable(readContent);
            Assert.True(content.SequenceEqual(readContentEnumerable));

            sharedMemoryMap.Dispose();
        }

        /// <summary>
        /// Create an empty content and try to read it back; verify that <see cref="SharedMemoryMap.GetStreamAsync"/>
        /// returns <see cref="null"/>.
        /// </summary>
        [Fact]
        public async Task GetStream_EmptyContent_VerifyContentMatches()
        {
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryMap sharedMemoryMap = Create(mapName, 0);
            byte[] content = TestUtils.GetRandomBytesInArray(0);

            long bytesWritten = await sharedMemoryMap.PutBytesAsync(content);
            Assert.Equal(0, bytesWritten);

            Stream readStream = await sharedMemoryMap.GetStreamAsync();
            Assert.Null(readStream);

            sharedMemoryMap.Dispose();
        }

        /// <summary>
        /// Create a <see cref="SharedMemoryMap"/> and put some content in it using <see cref="UnmanagedMemoryStream"/> and try to read it back.
        /// Since <see cref="UnmanagedMemoryStream"/> can hold more than 2GB data, we try it with larger inputs.
        /// Verify that the content read matches what was written.
        /// </summary>
        /// <param name="contentSize">Size of content to put inside the <see cref="SharedMemoryMap"/>.</param>
        [InlineData((long)2 * 1024 * 1024 * 1024)] // 2GB
        [InlineData((long)3 * 1024 * 1024 * 1024)] // 3GB
        [Theory(Skip = "Results in OOM when run on test infra")]
        public async Task GetStream_LargeData_VerifyContentMatches(long contentSize)
        {
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryMap sharedMemoryMap = Create(mapName, contentSize);

            // Since the size of memory being requested can be greater than 2GB, a 32 bit
            // integer type can't hold that. So we use an IntPtr to store the size, which
            // on a 64 bit platform would store the size in a 64 bit integer. Then we can pass
            // a pointer to that integer to the memory allocation method.
            IntPtr sizePtr = new IntPtr(contentSize);

            // Allocate a block of unmanaged memory
            IntPtr memIntPtr = Marshal.AllocHGlobal(sizePtr);

            // Generate content to put into the shared memory map
            Stream content = TestUtils.GetRandomContentInStream(contentSize, memIntPtr);

            // Put content into the shared memory map
            await sharedMemoryMap.PutStreamAsync(content);

            // Get content from the shared memory map
            Stream readContent = await sharedMemoryMap.GetStreamAsync();

            // Check if the read stream contains the same content that was written
            Assert.True(await TestUtils.StreamEqualsAsync(content, readContent));

            // Free the block of unmanaged memory
            Marshal.FreeHGlobal(memIntPtr);

            content.Close();
            content.Dispose();

            sharedMemoryMap.Dispose();
        }

        /// <summary>
        /// Create a <see cref="SharedMemoryMap"/> and put some content in it using <see cref="byte[]"/> and try to get the content length.
        /// Since <see cref="byte[]"/> cannot be more than 2GB, that's the maximum we will try to put.
        /// Verify that the content length matches that of what was written.
        /// </summary>
        /// <param name="contentSize">Size of content to put inside the <see cref="SharedMemoryMap"/>.</param>
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1024)] // 1KB
        [InlineData(1024 * 1024)] // 1MB
        [InlineData(256 * 1024 * 1024)] // 256MB
        [InlineData(1024 * 1024 * 1024, Skip = "Results in OOM when run on test infra")] // 1GB
        [Theory]
        public async Task GetContentLength_VerifyContentLengthMatches(int contentSize)
        {
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryMap sharedMemoryMap = Create(mapName, contentSize);
            byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

            await sharedMemoryMap.PutBytesAsync(content);
            Assert.Equal(contentSize, await sharedMemoryMap.GetContentLengthAsync());

            sharedMemoryMap.Dispose();
        }

        /// <summary>
        /// Create a <see cref="SharedMemoryMap"/> and put some content in it using <see cref="UnmanagedMemoryStream"/> and try to get the content length.
        /// Since <see cref="UnmanagedMemoryStream"/> can hold more than 2GB data, we try it with larger inputs.
        /// Verify that the content length matches that of what was written.
        /// </summary>
        /// <param name="contentSize">Size of content to put inside the <see cref="SharedMemoryMap"/>.</param>
        [InlineData((long)2 * 1024 * 1024 * 1024)] // 2GB
        [InlineData((long)3 * 1024 * 1024 * 1024)] // 3GB
        [Theory(Skip = "Results in OOM when run on test infra")]
        public async Task GetContentLength_LargeData_VerifyContentLengthMatches(long contentSize)
        {
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryMap sharedMemoryMap = Create(mapName, contentSize);

            // Since the size of memory being requested can be greater than 2GB, a 32 bit
            // integer type can't hold that. So we use an IntPtr to store the size, which
            // on a 64 bit platform would store the size in a 64 bit integer. Then we can pass
            // a pointer to that integer to the memory allocation method.
            IntPtr sizePtr = new IntPtr(contentSize);

            // Allocate a block of unmanaged memory
            IntPtr memIntPtr = Marshal.AllocHGlobal(sizePtr);

            // Generate content to put into the shared memory map
            Stream content = TestUtils.GetRandomContentInStream(contentSize, memIntPtr);

            // Put content into the shared memory map
            await sharedMemoryMap.PutStreamAsync(content);

            Assert.Equal(contentSize, await sharedMemoryMap.GetContentLengthAsync());

            sharedMemoryMap.Dispose();
        }

        [Fact]
        public void Dispose_DeleteMemoryMappedFile_VerifyDeleted()
        {
            long contentSize = 1024;
            string mapName = Guid.NewGuid().ToString();
            SharedMemoryMap sharedMemoryMap = Create(mapName, contentSize);

            sharedMemoryMap.Dispose(deleteFile: true);

            Assert.Null(Open(mapName));
        }

        /// <summary>
        /// Helper method to create a <see cref="SharedMemoryMap"/> for tests.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> backing the <see cref="SharedMemoryMap"/>.</param>
        /// <param name="contentSize">Size of <see cref="MemoryMappedFile"/> to allocate for the <see cref="SharedMemoryMap"/>.</param>
        /// <returns><see cref="SharedMemoryMap"/> that was created, <see cref="null"/> in case of failure.</returns>
        private SharedMemoryMap Create(string mapName, long contentSize)
        {
            long size = contentSize + SharedMemoryConstants.HeaderTotalBytes;
            if (_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf))
            {
                return new SharedMemoryMap(_loggerFactory, _mapAccessor, mapName, mmf);
            }

            return null;
        }

        /// <summary>
        /// Helper method to open a <see cref="SharedMemoryMap"/> for tests.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> backing the <see cref="SharedMemoryMap"/>.</param>
        /// <returns><see cref="SharedMemoryMap"/> that was opened, <see cref="null"/> in case of failure.</returns>
        private SharedMemoryMap Open(string mapName)
        {
            if (_mapAccessor.TryOpen(mapName, out MemoryMappedFile mmf))
            {
                return new SharedMemoryMap(_loggerFactory, _mapAccessor, mapName, mmf);
            }

            return null;
        }
    }
}
