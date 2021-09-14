// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.FunctionDataCache;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers
{
    /// <summary>
    /// Tests for <see cref="FunctionDataCacheTests"/>.
    /// </summary>
    public class FunctionDataCacheTests
    {
        private readonly ILoggerFactory _loggerFactory;

        private readonly IEnvironment _testEnvironment;

        private readonly IMemoryMappedFileAccessor _mapAccessor;

        public FunctionDataCacheTests()
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

            _testEnvironment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");
        }

        [InlineData("1")]
        [InlineData("true")]
        [InlineData("True")]
        [Theory]
        public void ToggleEnabled_VerifyIsEnabled(string envVal)
        {
            IEnvironment testEnvironment = new TestEnvironment();

            testEnvironment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, envVal);

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, testEnvironment))
            {
                Assert.True(cache.IsEnabled);
            }
        }

        [InlineData("")]
        [InlineData("0")]
        [InlineData("false")]
        [InlineData("False")]
        [Theory]
        public void ToggleDisabled_VerifyIsEnabled(string envVal)
        {
            IEnvironment testEnvironment = new TestEnvironment();

            testEnvironment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, envVal);

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, testEnvironment))
            {
                Assert.False(cache.IsEnabled);
            }
        }

        [InlineData(1)] // 1B
        [InlineData(1 * 1024)] // 1KB
        [InlineData(15 * 1024 * 1024)] // 15MB
        [InlineData(256 * 1024 * 1024)] // 256MB
        [Theory]
        public void SetValidCacheSizeInEnvironment_VerifyMaximumCapacityBytes(long cacheSize)
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");

            testEnvironment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheMaximumSizeBytesSettingName, $"{cacheSize}");

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, testEnvironment))
            {
                Assert.Equal(cacheSize, cache.RemainingCapacityBytes);
            }
        }

        [InlineData("-1")]
        [InlineData("0")]
        [InlineData("")]
        [Theory]
        public void SetInvalidCacheSizeInEnvironment_VerifyMaximumCapacityBytes(string cacheSize)
        {
            IEnvironment testEnvironment = new TestEnvironment();
            testEnvironment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");

            testEnvironment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheMaximumSizeBytesSettingName, cacheSize);

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, testEnvironment))
            {
                Assert.Equal(FunctionDataCacheConstants.FunctionDataCacheDefaultMaximumSizeBytes, cache.RemainingCapacityBytes);
            }
        }

        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1024)] // 1KB
        [InlineData(1024 * 1024)] // 1MB
        [InlineData(15 * 1024 * 1024)] // 15MB
        [Theory]
        public async Task PutObject_NoEvictions_VerifyGet(int contentSize)
        {
            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, _testEnvironment))
            {
                // Prepare content
                byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

                // Put into shared memory
                SharedMemoryMetadata metadata = await manager.PutObjectAsync(content);

                // Put into cache
                FunctionDataCacheKey key = new FunctionDataCacheKey("foo", "bar");
                Assert.True(cache.TryPut(key, metadata, isIncrementActiveReference: false, isDeleteOnFailure: false));

                // Get from cache
                Assert.True(cache.TryGet(key, isIncrementActiveReference: false, out SharedMemoryMetadata getMetadata));

                // Compare if the obtained values are equal
                Assert.Equal(metadata, getMetadata);
            }
        }

        [Fact]
        public async Task PutThreeObjects_VerifyLRUOrder()
        {
            int contentSize = 2 * 1024 * 1024; // 2MB

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, _testEnvironment))
            {
                // Prepare content
                byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

                // Put into shared memory as three distinct objects
                List<SharedMemoryMetadata> metadatas = new List<SharedMemoryMetadata>()
                {
                    await manager.PutObjectAsync(content),
                    await manager.PutObjectAsync(content),
                    await manager.PutObjectAsync(content),
                };

                // Put the three objects into the cache
                List<FunctionDataCacheKey> keys = new List<FunctionDataCacheKey>()
                {
                    new FunctionDataCacheKey("foo_1", "bar_1"),
                    new FunctionDataCacheKey("foo_2", "bar_2"),
                    new FunctionDataCacheKey("foo_3", "bar_3"),
                };
                for (int i = 0; i < 3; i++)
                {
                    Assert.True(cache.TryPut(keys[i], metadatas[i], isIncrementActiveReference: false, isDeleteOnFailure: false));
                }

                // The order of the LRU list should be the same as that in which objects were inserted above.
                // i.e. the first element of the list should be the least recently used (oldest inserted).
                int pos = 1;
                foreach (FunctionDataCacheKey key in cache.LRUList)
                {
                    string keyId = key.Id;
                    int num = int.Parse(keyId.Split("_")[1]);
                    Assert.Equal(pos, num);
                    pos++;
                }
            }
        }

        [Fact]
        public async Task PutThreeObjects_GetOne_VerifyLRUOrder()
        {
            int contentSize = 2 * 1024 * 1024; // 2MB

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, _testEnvironment))
            {
                // Prepare content
                byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

                // Put into shared memory as three distinct objects
                List<SharedMemoryMetadata> metadatas = new List<SharedMemoryMetadata>()
                {
                    await manager.PutObjectAsync(content),
                    await manager.PutObjectAsync(content),
                    await manager.PutObjectAsync(content),
                };

                // Put the three objects into the cache
                List<FunctionDataCacheKey> keys = new List<FunctionDataCacheKey>()
                {
                    new FunctionDataCacheKey("foo_1", "bar_1"),
                    new FunctionDataCacheKey("foo_2", "bar_2"),
                    new FunctionDataCacheKey("foo_3", "bar_3"),
                };
                for (int i = 0; i < 3; i++)
                {
                    Assert.True(cache.TryPut(keys[i], metadatas[i], isIncrementActiveReference: false, isDeleteOnFailure: false));
                }

                // Access the middle object so that now it is the most recently used
                Assert.True(cache.TryGet(keys[1], isIncrementActiveReference: false, out var _));

                // The order of objects in LRU should be 1->3->2.
                // Since 2 is the most recently used, it is add the end of the list.
                int pos = 1;
                foreach (FunctionDataCacheKey key in cache.LRUList)
                {
                    string keyId = key.Id;
                    int num = int.Parse(keyId.Split("_")[1]);

                    int expected;
                    if (pos == 1)
                    {
                        expected = 1;
                    }
                    else if (pos == 2)
                    {
                        expected = 3;
                    }
                    else if (pos == 3)
                    {
                        expected = 2;
                    }
                    else
                    {
                        throw new Exception("Unexpected position");
                    }

                    Assert.Equal(expected, num);
                    pos++;
                }
            }
        }

        [Fact]
        public async Task PutThreeObjects_GetOne_NoActiveReferences_VerifyEvictionOrder()
        {
            int contentSize = 2 * 1024 * 1024; // 2MB

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, _testEnvironment))
            {
                // Prepare content
                byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

                // Put into shared memory as three distinct objects
                List<SharedMemoryMetadata> metadatas = new List<SharedMemoryMetadata>()
                {
                    await manager.PutObjectAsync(content),
                    await manager.PutObjectAsync(content),
                    await manager.PutObjectAsync(content),
                };

                // Put the three objects into the cache
                List<FunctionDataCacheKey> keys = new List<FunctionDataCacheKey>()
                {
                    new FunctionDataCacheKey("foo_1", "bar_1"),
                    new FunctionDataCacheKey("foo_2", "bar_2"),
                    new FunctionDataCacheKey("foo_3", "bar_3"),
                };
                for (int i = 0; i < 3; i++)
                {
                    Assert.True(cache.TryPut(keys[i], metadatas[i], isIncrementActiveReference: false, isDeleteOnFailure: false));
                }

                // Access the middle object so that now it is the most recently used
                Assert.True(cache.TryGet(keys[1], isIncrementActiveReference: false, out var _));

                // The order of objects in LRU should be 1->3->2.
                // Since 2 is the most recently used, it is at the end of the list.

                // Evict an object and check that the right object was evicted (1)
                Assert.True(cache.EvictOne());
                Assert.False(cache.TryGet(keys[0], isIncrementActiveReference: false, out var _));
                // Check if the other objects are still present;
                // We cannot check using TryGet as that will impact the LRU ordering
                Assert.Contains(keys[1], cache.LRUList);
                Assert.Contains(keys[2], cache.LRUList);

                // Evict an object and check that the right object was evicted (3)
                Assert.True(cache.EvictOne());
                Assert.False(cache.TryGet(keys[2], isIncrementActiveReference: false, out var _));
                // Check if the other object are still present;
                // We cannot check using TryGet as that will impact the LRU ordering
                Assert.Contains(keys[1], cache.LRUList);

                // Evict an object and check that the right object was evicted (2)
                Assert.True(cache.EvictOne());
                Assert.Empty(cache.LRUList);
            }
        }

        [Fact]
        public async Task PutObject_ActiveReference_VerifyNotEvicted()
        {
            int contentSize = 2 * 1024 * 1024; // 2MB
            int cacheSize = 3 * 1024 * 1024; // 3MB
            string cacheSizeVal = cacheSize.ToString();

            IEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheMaximumSizeBytesSettingName, cacheSizeVal);
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, environment))
            {
                // Prepare content
                byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

                // Put into shared memory as two objects
                SharedMemoryMetadata metadata1 = await manager.PutObjectAsync(content);
                SharedMemoryMetadata metadata2 = await manager.PutObjectAsync(content);

                // Put one object into the cache and keep an active reference
                FunctionDataCacheKey key1 = new FunctionDataCacheKey("foo1", "bar1");
                Assert.True(cache.TryPut(key1, metadata1, isIncrementActiveReference: true, isDeleteOnFailure: false));

                // The first object has used up the cache space.
                // When trying to insert the second object into the cache, it should fail
                // since the first has an active reference and cannot be evicted.
                FunctionDataCacheKey key2 = new FunctionDataCacheKey("foo2", "bar2");
                Assert.False(cache.TryPut(key2, metadata2, isIncrementActiveReference: false, isDeleteOnFailure: false));
                // Ensure that the first object was not evicted
                Assert.True(cache.TryGet(key1, isIncrementActiveReference: false, out var _));

                // Drop the active reference on the first object
                cache.DecrementActiveReference(key1);

                // Now, when trying to insert the second object into the cache, it should succeed
                // since the first object can be evicted (since its active reference was dropped).
                Assert.True(cache.TryPut(key2, metadata2, isIncrementActiveReference: false, isDeleteOnFailure: false));
                // Ensure that the first object was evicted
                Assert.False(cache.TryGet(key1, isIncrementActiveReference: false, out var _));
            }
        }

        [Fact]
        public async Task PutObject_ActiveReference_VerifyCorrectObjectEvicted()
        {
            int contentSize = 2 * 1024 * 1024; // 2MB
            int cacheSize = 6 * 1024 * 1024; // 6MB
            string cacheSizeVal = cacheSize.ToString();

            IEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheMaximumSizeBytesSettingName, cacheSizeVal);
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, environment))
            {
                // Prepare content
                byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

                // Put into shared memory as three distinct objects
                List<SharedMemoryMetadata> metadatas = new List<SharedMemoryMetadata>()
                {
                    await manager.PutObjectAsync(content),
                    await manager.PutObjectAsync(content),
                    await manager.PutObjectAsync(content),
                };

                // Put the three objects into the cache
                List<FunctionDataCacheKey> keys = new List<FunctionDataCacheKey>()
                {
                    new FunctionDataCacheKey("foo_1", "bar_1"),
                    new FunctionDataCacheKey("foo_2", "bar_2"),
                    new FunctionDataCacheKey("foo_3", "bar_3"),
                };

                // Put object (1) with an active reference
                Assert.True(cache.TryPut(keys[0], metadatas[0], isIncrementActiveReference: true, isDeleteOnFailure: false));

                // Put objects (2) and (3) with NO active reference
                Assert.True(cache.TryPut(keys[1], metadatas[1], isIncrementActiveReference: false, isDeleteOnFailure: false));
                Assert.True(cache.TryPut(keys[2], metadatas[2], isIncrementActiveReference: false, isDeleteOnFailure: false));

                // Access the middle object so that now it is the most recently used
                Assert.True(cache.TryGet(keys[1], isIncrementActiveReference: false, out var _));

                // The order of objects in LRU should be 1->3->2.
                // Since 2 is the most recently used, it is at the end of the list.

                // Evict an object and check that the right object was evicted (3)
                // Object (1) will not be evicted because it has an active reference even though it is before object (3) in the LRU list.
                Assert.True(cache.EvictOne());
                Assert.False(cache.TryGet(keys[2], isIncrementActiveReference: false, out var _));
                // Check if the other objects are still present;
                // We cannot check using TryGet as that will impact the LRU ordering
                Assert.Contains(keys[0], cache.LRUList);
                Assert.Contains(keys[1], cache.LRUList);

                // Evict an object and check that the right object was evicted (2)
                Assert.True(cache.EvictOne());
                Assert.False(cache.TryGet(keys[1], isIncrementActiveReference: false, out var _));
                // Check if the other object are still present;
                // We cannot check using TryGet as that will impact the LRU ordering
                Assert.Contains(keys[0], cache.LRUList);

                // The only object left is object (1) but it has an active reference so cannot be evicted
                Assert.False(cache.EvictOne());
                Assert.Contains(keys[0], cache.LRUList);

                // Decrement the reference on object (1)
                cache.DecrementActiveReference(keys[0]);

                // Now object (1) should be evicted and the cache should be empty
                Assert.True(cache.EvictOne());
                Assert.Empty(cache.LRUList);
                Assert.Empty(cache.ActiveReferences);
                Assert.Equal(cacheSize, cache.RemainingCapacityBytes);
            }
        }

        [Fact]
        public async Task PutObject_NoActiveReferences_ForceOneEviction_VerifyCorrectEviction()
        {
            int contentSize = 2 * 1024 * 1024; // 2MB
            int cacheSize = 6 * 1024 * 1024; // 6MB
            string cacheSizeVal = cacheSize.ToString();

            IEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheMaximumSizeBytesSettingName, cacheSizeVal);
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, environment))
            {
                // Prepare content
                byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

                // Put into shared memory as three distinct objects
                SharedMemoryMetadata metadata1 = await manager.PutObjectAsync(content);
                SharedMemoryMetadata metadata2 = await manager.PutObjectAsync(content);
                SharedMemoryMetadata metadata3 = await manager.PutObjectAsync(content);

                // Put the three objects into the cache
                FunctionDataCacheKey key1 = new FunctionDataCacheKey("foo1", "bar1");
                FunctionDataCacheKey key2 = new FunctionDataCacheKey("foo2", "bar2");
                FunctionDataCacheKey key3 = new FunctionDataCacheKey("foo3", "bar3");
                Assert.True(cache.TryPut(key1, metadata1, isIncrementActiveReference: false, isDeleteOnFailure: false));
                Assert.True(cache.TryPut(key2, metadata2, isIncrementActiveReference: false, isDeleteOnFailure: false));
                Assert.True(cache.TryPut(key3, metadata3, isIncrementActiveReference: false, isDeleteOnFailure: false));

                // Verify that the cache is full
                Assert.Equal(0, cache.RemainingCapacityBytes);

                // At this point, the cache is full.
                // We will create another object and try to insert it.
                // This should be inserted (as another object will be evicted to make room for this).
                SharedMemoryMetadata metadata4 = await manager.PutObjectAsync(content);
                FunctionDataCacheKey key4 = new FunctionDataCacheKey("foo4", "bar4");
                Assert.True(cache.TryPut(key4, metadata4, isIncrementActiveReference: false, isDeleteOnFailure: false));

                // The first object should be evicted (least recently used) by now
                Assert.False(cache.TryGet(key1, isIncrementActiveReference: false, out var _));

                // Try to open the shared memory map of the first object and ensure it is removed and cannot be opened
                Assert.False(_mapAccessor.TryOpen(metadata1.MemoryMapName, out var _));

                // The last three objects (the first two added before eviction and the one resulting in eviction) should be present
                Assert.True(cache.TryGet(key2, isIncrementActiveReference: false, out var _));
                Assert.True(cache.TryGet(key3, isIncrementActiveReference: false, out var _));
                Assert.True(cache.TryGet(key4, isIncrementActiveReference: false, out var _));

                // Verify that the cache is full
                Assert.Equal(0, cache.RemainingCapacityBytes);
            }
        }

        [Fact]
        public async Task PutObject_NoActiveReferences_ForceMultipleEvictions_VerifyCorrectEvictions()
        {
            int contentSizeInitial = 2 * 1024 * 1024; // 2MB
            int cacheSize = 6 * 1024 * 1024; // 6MB
            int contentSizeFinal = 5 * 1024 * 1024; // 5MB
            string cacheSizeVal = cacheSize.ToString();

            IEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheMaximumSizeBytesSettingName, cacheSizeVal);
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");

            using (ISharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, environment))
            {
                // Prepare content
                byte[] contentInitial = TestUtils.GetRandomBytesInArray(contentSizeInitial);

                // Put into shared memory as three distinct objects
                SharedMemoryMetadata metadata1 = await manager.PutObjectAsync(contentInitial);
                SharedMemoryMetadata metadata2 = await manager.PutObjectAsync(contentInitial);

                // Put the two objects into the cache
                FunctionDataCacheKey key1 = new FunctionDataCacheKey("foo1", "bar1");
                FunctionDataCacheKey key2 = new FunctionDataCacheKey("foo2", "bar2");
                Assert.True(cache.TryPut(key1, metadata1, isIncrementActiveReference: false, isDeleteOnFailure: false));
                Assert.True(cache.TryPut(key2, metadata2, isIncrementActiveReference: false, isDeleteOnFailure: false));

                // At this point, the cache is full.
                // We will create another object and try to insert it.
                byte[] contentFinal = TestUtils.GetRandomBytesInArray(contentSizeFinal);
                SharedMemoryMetadata metadata3 = await manager.PutObjectAsync(contentFinal);
                FunctionDataCacheKey key3 = new FunctionDataCacheKey("foo3", "bar3");
                Assert.True(cache.TryPut(key3, metadata3, isIncrementActiveReference: false, isDeleteOnFailure: false));

                // The first two objects should be evicted by now
                Assert.False(cache.TryGet(key1, isIncrementActiveReference: false, out var _));
                Assert.False(cache.TryGet(key2, isIncrementActiveReference: false, out var _));

                // Try to open the shared memory map of the first object and ensure it is removed and cannot be opened
                Assert.False(_mapAccessor.TryOpen(metadata1.MemoryMapName, out var _));
                Assert.False(_mapAccessor.TryOpen(metadata2.MemoryMapName, out var _));

                // The last inserted object should be present
                Assert.True(cache.TryGet(key3, isIncrementActiveReference: false, out var _));

                // Verify that the cache has 1MB remaining
                Assert.Equal(1 * 1024 * 1024, cache.RemainingCapacityBytes);
            }
        }

        [Fact]
        public async Task PutObject_FailToPut_DeleteOnFailure()
        {
            int contentSize = 4 * 1024 * 1024; // 4MB
            int cacheSize = 3 * 1024 * 1024; // 3MB
            string cacheSizeVal = cacheSize.ToString();

            IEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheMaximumSizeBytesSettingName, cacheSizeVal);
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");

            using (SharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, environment))
            {
                // Prepare content
                byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

                // Put into shared memory
                SharedMemoryMetadata metadata = await manager.PutObjectAsync(content);

                // Try to put the object into the cache; this will fail because the cache is smaller than the object size.
                // Since isDeleteOnFailure is true, the object will be deleted from shared memory.
                FunctionDataCacheKey key = new FunctionDataCacheKey("foo", "bar");
                Assert.False(cache.TryPut(key, metadata, isIncrementActiveReference: true, isDeleteOnFailure: true));

                // Ensure that nothing was cached and no references are held
                Assert.Empty(cache.LRUList);
                Assert.Empty(cache.ActiveReferences);
                Assert.Equal(cacheSize, cache.RemainingCapacityBytes);

                // Ensure that the SharedMemoryManager does not have any maps allocated
                Assert.Empty(manager.AllocatedSharedMemoryMaps);

                // Try to open the shared memory map of the first object and ensure it is removed and cannot be opened
                Assert.False(_mapAccessor.TryOpen(metadata.MemoryMapName, out var _));
            }
        }

        [Fact]
        public async Task PutObject_FailToPut_DoNotDeleteOnFailure()
        {
            int contentSize = 4 * 1024 * 1024; // 4MB
            int cacheSize = 3 * 1024 * 1024; // 3MB
            string cacheSizeVal = cacheSize.ToString();

            IEnvironment environment = new TestEnvironment();
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheMaximumSizeBytesSettingName, cacheSizeVal);
            environment.SetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName, "1");

            using (SharedMemoryManager manager = new SharedMemoryManager(_loggerFactory, _mapAccessor))
            using (FunctionDataCache cache = new FunctionDataCache(manager, _loggerFactory, environment))
            {
                // Prepare content
                byte[] content = TestUtils.GetRandomBytesInArray(contentSize);

                // Put into shared memory
                SharedMemoryMetadata metadata = await manager.PutObjectAsync(content);

                // Try to put the object into the cache; this will fail because the cache is smaller than the object size.
                // Since isDeleteOnFailure is false, the object will not be deleted from shared memory.
                FunctionDataCacheKey key = new FunctionDataCacheKey("foo", "bar");
                Assert.False(cache.TryPut(key, metadata, isIncrementActiveReference: true, isDeleteOnFailure: false));

                // Ensure that nothing was cached and no references are held
                Assert.Empty(cache.LRUList);
                Assert.Empty(cache.ActiveReferences);
                Assert.Equal(cacheSize, cache.RemainingCapacityBytes);

                // Ensure that the SharedMemoryManager has the allocated memory map and it was not deleted
                Assert.Equal(1, manager.AllocatedSharedMemoryMaps.Count);

                // Try to open the shared memory map of the first object and ensure it exists and can be opened
                Assert.True(_mapAccessor.TryOpen(metadata.MemoryMapName, out var _));
            }
        }
    }
}
