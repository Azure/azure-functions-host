// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Extensions;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    /// <summary>
    /// Tests for <see cref="RpcSharedMemory"/>.
    /// </summary>
    public class RpcSharedMemoryTests
    {
        private readonly IMemoryMappedFileAccessor _mapAccessor;

        private readonly ILoggerFactory _loggerFactory;

        private readonly IEnvironment _testEnvironment;

        public RpcSharedMemoryTests()
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
        /// Helper method to create a <see cref="SharedMemoryMap"/> for tests.
        /// </summary>
        /// <param name="mapName">Name of the <see cref="MemoryMappedFile"/> backing the <see cref="SharedMemoryMap"/>.</param>
        /// <param name="contentSize">Size of <see cref="MemoryMappedFile"/> to allocate for the <see cref="SharedMemoryMap"/>.</param>
        /// <returns><see cref="SharedMemoryMap"/> that was created, <see cref="null"/> in case of failure.</returns>
        private SharedMemoryMap CreateSharedMemoryMap(string mapName, long contentSize)
        {
            long size = contentSize + SharedMemoryConstants.HeaderTotalBytes;
            if (_mapAccessor.TryCreate(mapName, size, out MemoryMappedFile mmf))
            {
                return new SharedMemoryMap(_loggerFactory, _mapAccessor, mapName, mmf);
            }

            return null;
        }

        /// <summary>
        /// Try to create an object in shared memory.
        /// Request that object to be converted to a higher level type using <see cref="RpcSharedMemory"/>.
        /// Verify that the correct object type is returned.
        /// </summary>
        [Fact]
        public async Task ToObject_Bytes_FunctionDataCacheEnabled_VerifySuccess()
        {
            ILogger<RpcSharedMemory> logger = NullLogger<RpcSharedMemory>.Instance;
            string invocationId = Guid.NewGuid().ToString();
            long contentSize = 16 * 1024; // 16KB, enough to try out the functionality we need to test

            using (SharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            {
                // Create a SharedMemoryMap
                string mapName = Guid.NewGuid().ToString();
                SharedMemoryMap sharedMemoryMap = CreateSharedMemoryMap(mapName, contentSize);

                // Wite content into it
                byte[] content = TestUtils.GetRandomBytesInArray((int)contentSize);
                long bytesWritten = await sharedMemoryMap.PutBytesAsync(content);
                Assert.Equal(contentSize, bytesWritten);

                // Create a RpcSharedMemory object pointing to the shared memory region we created earlier
                RpcSharedMemory rpcSharedMemory = new RpcSharedMemory
                {
                    Name = mapName,
                    Count = contentSize,
                    Offset = 0,
                    Type = RpcDataType.Bytes
                };

                // Convert RpcSharedMemory object into byte[]
                object rpcObj = await rpcSharedMemory.ToObjectAsync(logger, invocationId, manager, isFunctionDataCacheEnabled: true);
                Assert.NotNull(rpcObj);

                // Since the FunctionDataCache is enabled, the object should be a SharedMemoryObject
                Assert.IsType<SharedMemoryObject>(rpcObj);
                SharedMemoryObject sharedMemoryObject = rpcObj as SharedMemoryObject;

                // Verify that the read object is correct
                Assert.Equal(mapName, sharedMemoryObject.MemoryMapName);
                Assert.Equal(contentSize, sharedMemoryObject.Count);

                // Since the FunctionDataCache is enabled, ensure that the SharedMemoryManager is tracking the object that was read
                Assert.Equal(1, manager.AllocatedSharedMemoryMaps.Count);
                Assert.True(manager.AllocatedSharedMemoryMaps.TryGetValue(mapName, out _));

                // Dispose off the created resources
                sharedMemoryMap.Dispose();
            }
        }

        /// <summary>
        /// Try to create an object in shared memory.
        /// Request that object to be converted to a higher level type using <see cref="RpcSharedMemory"/>.
        /// Verify that the correct object type is returned.
        /// </summary>
        [Fact]
        public async Task ToObject_Bytes_FunctionDataCacheDisabled_VerifySuccess()
        {
            ILogger<RpcSharedMemory> logger = NullLogger<RpcSharedMemory>.Instance;
            string invocationId = Guid.NewGuid().ToString();
            long contentSize = 16 * 1024; // 16KB, enough to try out the functionality we need to test

            using (SharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            {
                // Create a SharedMemoryMap
                string mapName = Guid.NewGuid().ToString();
                SharedMemoryMap sharedMemoryMap = CreateSharedMemoryMap(mapName, contentSize);

                // Wite content into it
                byte[] content = TestUtils.GetRandomBytesInArray((int)contentSize);
                long bytesWritten = await sharedMemoryMap.PutBytesAsync(content);
                Assert.Equal(contentSize, bytesWritten);

                // Create a RpcSharedMemory object pointing to the shared memory region we created earlier
                RpcSharedMemory rpcSharedMemory = new RpcSharedMemory
                {
                    Name = mapName,
                    Count = contentSize,
                    Offset = 0,
                    Type = RpcDataType.Bytes
                };

                // Convert RpcSharedMemory object into byte[]
                object rpcObj = await rpcSharedMemory.ToObjectAsync(logger, invocationId, manager, isFunctionDataCacheEnabled: false);
                Assert.NotNull(rpcObj);

                // Since the FunctionDataCache is disabled, the object should be byte[]
                Assert.IsType<byte[]>(rpcObj);
                byte[] rpcObjBytes = rpcObj as byte[];

                // Verify that the read object is correct
                Assert.Equal(content, rpcObjBytes);

                // Since the FunctionDataCache is disabled, ensure that the SharedMemoryManager is not tracking the object that was read
                Assert.Empty(manager.AllocatedSharedMemoryMaps);

                // Dispose off the created resources
                sharedMemoryMap.Dispose();
            }
        }

        /// <summary>
        /// Try to create an object in shared memory.
        /// Request that object to be converted to a higher level type using <see cref="RpcSharedMemory"/>.
        /// Request using an unsupported type and verify that the conversion fails.
        /// </summary>
        /// <param name="isFunctionDataCacheEnabled">Whether to enable the use of <see cref="IFunctionDataCache"/> or not.</param>
        [InlineData(true)]
        [InlineData(false)]
        [Theory]
        public async Task ToObject_CollectionBytes_VerifyFailure(bool isFunctionDataCacheEnabled)
        {
            ILogger<RpcSharedMemory> logger = NullLogger<RpcSharedMemory>.Instance;
            string invocationId = Guid.NewGuid().ToString();
            long contentSize = 16 * 1024; // 16KB, enough to try out the functionality we need to test

            using (SharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            {
                // Create a SharedMemoryMap
                string mapName = Guid.NewGuid().ToString();
                SharedMemoryMap sharedMemoryMap = CreateSharedMemoryMap(mapName, contentSize);

                // Wite content into it
                byte[] content = TestUtils.GetRandomBytesInArray((int)contentSize);
                long bytesWritten = await sharedMemoryMap.PutBytesAsync(content);
                Assert.Equal(contentSize, bytesWritten);

                // Create a RpcSharedMemory object pointing to the shared memory region we created earlier
                // Although the type is not correct, instead of Bytes it is CollectionBytes (unsupported)
                RpcSharedMemory rpcSharedMemory = new RpcSharedMemory
                {
                    Name = mapName,
                    Count = contentSize,
                    Offset = 0,
                    Type = RpcDataType.CollectionBytes
                };

                // Try to convert the object but this should fail since the type we are requested is unsupported
                await Assert.ThrowsAsync<InvalidDataException>(async () => await rpcSharedMemory.ToObjectAsync(logger, invocationId, manager, isFunctionDataCacheEnabled));

                // Dispose off the created resources
                sharedMemoryMap.Dispose();
            }
        }
    }
}
