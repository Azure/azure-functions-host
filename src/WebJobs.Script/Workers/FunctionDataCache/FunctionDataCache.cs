// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.FunctionDataCache
{
    public class FunctionDataCache : IFunctionDataCache
    {
        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Shared memory manager which manages the backing memory regions for the cache.
        /// </summary>
        private readonly ISharedMemoryManager _sharedMemoryManager;

        /// <summary>
        /// Lock to be used with any operations that access state of this cache.
        /// </summary>
        private readonly object _lock;

        /// <summary>
        /// Mapping of <see cref="FunctionDataCacheKey"/> to the <see cref="SharedMemoryMetadata"/>
        /// which indicates where in shared memory the object exists.
        /// Note: This must be accessed while holding the <see cref="_lock"/> lock.
        /// </summary>
        private readonly Dictionary<FunctionDataCacheKey, SharedMemoryMetadata> _localCache;

        /// <summary>
        /// Maximum number of bytes of data that can be stored in the cache.
        /// </summary>
        private readonly long _maximumCapacityBytes;

        public FunctionDataCache(ISharedMemoryManager sharedMemoryManager, ILoggerFactory loggerFactory, IEnvironment environment)
        {
            _logger = loggerFactory.CreateLogger<FunctionDataCache>();
            _sharedMemoryManager = sharedMemoryManager;
            _maximumCapacityBytes = GetMaximumCapacityBytes(environment);
            _lock = new object();
            _localCache = new Dictionary<FunctionDataCacheKey, SharedMemoryMetadata>();
            LRUList = new LinkedList<FunctionDataCacheKey>();
            ActiveReferences = new Dictionary<FunctionDataCacheKey, long>();
            RemainingCapacityBytes = _maximumCapacityBytes;
            IsEnabled = GetIsEnabled(environment);
        }

        public bool IsEnabled { get; private set; }

        /// <summary>
        /// Gets the total free capacity of the cache in number of bytes.
        /// </summary>
        internal long RemainingCapacityBytes { get; private set; }

        /// <summary>
        /// Gets the list maintaining the order of use of <see cref="FunctionDataCacheKey"/> in an LRU fashion.
        /// The last element of the list is the most recently used item.
        /// The first element of the list is the least recently used item.
        /// </summary>
        internal LinkedList<FunctionDataCacheKey> LRUList { get; private set; }

        /// <summary>
        /// Gets the mapping of active reference count for each <see cref="FunctionDataCacheKey"/> in the cache.
        /// Note: Only <see cref="FunctionDataCacheKey"/> with at least one active reference count are maintained in this data structure.
        /// </summary>
        internal Dictionary<FunctionDataCacheKey, long> ActiveReferences { get; private set; }

        public bool TryPut(FunctionDataCacheKey cacheKey, SharedMemoryMetadata sharedMemoryMeta, bool isIncrementActiveReference, bool isDeleteOnFailure)
        {
            bool isFailure = true;

            try
            {
                lock (_lock)
                {
                    // Check if the key is already present in the cache
                    if (_localCache.ContainsKey(cacheKey))
                    {
                        // Key already exists in the local cache; do not overwrite and don't delete the existing data.
                        _logger.LogTrace("Cannot insert object into cache, it already exists: {ObjectName} and version: {Version}", cacheKey.Id, cacheKey.Version);

                        isFailure = false;
                        return false;
                    }

                    long bytesRequired = sharedMemoryMeta.Count;
                    if (!EvictUntilCapacityAvailable(bytesRequired))
                    {
                        _logger.LogTrace("Cannot insert object into cache, not enough space (required: {RequiredBytes} < available: {CapacityBytes})", bytesRequired, RemainingCapacityBytes);

                        return false;
                    }

                    // Add the mapping into the local cache
                    _localCache.Add(cacheKey, sharedMemoryMeta);

                    // Update the LRU list (mark this key as the most recently used)
                    AddToEndOfLRU(cacheKey);

                    if (isIncrementActiveReference)
                    {
                        IncrementActiveReference(cacheKey);
                    }

                    // Update the cache utilization
                    RemainingCapacityBytes -= sharedMemoryMeta.Count;

                    _logger.LogTrace("Object inserted into cache: {ObjectName} and version: {Version} with size: {Size} in shared memory map: {MapName} with updated capacity: {CapacityBytes} bytes", cacheKey.Id, cacheKey.Version, sharedMemoryMeta.Count, sharedMemoryMeta.MemoryMapName, RemainingCapacityBytes);

                    isFailure = false;
                    return true;
                }
            }
            finally
            {
                if (isFailure && isDeleteOnFailure)
                {
                    if (!_sharedMemoryManager.TryFreeSharedMemoryMap(sharedMemoryMeta.MemoryMapName))
                    {
                        _logger.LogTrace("Cannot free shared memory map: {MapName} with size: {Size} bytes on failure to insert into the cache", sharedMemoryMeta.MemoryMapName, sharedMemoryMeta.Count);
                    }
                }
            }
        }

        public bool TryGet(FunctionDataCacheKey cacheKey, bool isIncrementActiveReference, out SharedMemoryMetadata sharedMemoryMeta)
        {
            lock (_lock)
            {
                // Get the value from the local cache
                if (!_localCache.TryGetValue(cacheKey, out sharedMemoryMeta))
                {
                    // Key does not exist in the local cache
                    _logger.LogTrace("Cache miss for object: {ObjectName} and version: {Version}", cacheKey.Id, cacheKey.Version);

                    return false;
                }

                // Update the LRU list (mark this key as the most recently used)
                AddToEndOfLRU(cacheKey);

                if (isIncrementActiveReference)
                {
                    IncrementActiveReference(cacheKey);
                }

                _logger.LogTrace("Cache hit for object: {ObjectName} and version: {Version} with size: {Size} in shared memory map: {MapName}", cacheKey.Id, cacheKey.Version, sharedMemoryMeta.Count, sharedMemoryMeta.MemoryMapName);

                return true;
            }
        }

        /// <summary>
        /// Note: This will remove the entry even if it has active references. It is the responsibility of the caller to
        /// ensure the entry is safe to be removed.
        /// </summary>
        /// <param name="cacheKey">The <see cref="FunctionDataCacheKey"/> corresponding to the entry to be removed.</param>
        /// <returns><see cref="true"/> if the entry was successfully removed, <see cref="false"/> if not.</returns>
        public bool TryRemove(FunctionDataCacheKey cacheKey)
        {
            lock (_lock)
            {
                if (!TryGet(cacheKey, isIncrementActiveReference: false, out SharedMemoryMetadata sharedMemoryMeta))
                {
                    return false;
                }

                // Remove the key from the local cache
                if (!_localCache.Remove(cacheKey))
                {
                    // Key does not exist in the local cache
                    return false;
                }

                // Free the shared memory containing data for the given key that is being removed
                if (!_sharedMemoryManager.TryFreeSharedMemoryMap(sharedMemoryMeta.MemoryMapName))
                {
                    // Unable to free the shared memory
                    return false;
                }

                // Remove from LRU list
                RemoveFromLRU(cacheKey);

                // Remove the key from the list of active references
                ActiveReferences.Remove(cacheKey);

                // Update the cache utilization
                RemainingCapacityBytes += sharedMemoryMeta.Count;

                _logger.LogTrace("Removed cache object: {ObjectName} and version: {Version} with size: {Size} in shared memory map: {MapName} with updated capacity: {CapacityBytes} bytes", cacheKey.Id, cacheKey.Version, sharedMemoryMeta.Count, sharedMemoryMeta.MemoryMapName, RemainingCapacityBytes);

                return true;
            }
        }

        public void DecrementActiveReference(FunctionDataCacheKey cacheKey)
        {
            lock (_lock)
            {
                DecrementActiveReferenceCore(cacheKey);
            }
        }

        public void Dispose()
        {
            _logger.LogTrace("Disposing FunctionDataCache");
        }

        /// <summary>
        /// Evicts the least recently used object from the cache such that the object has no active references.
        /// Note: Assumes that it is called in a thread-safe manner (i.e. <see cref="_lock"/> is held
        /// by the caller.)
        /// </summary>
        /// <returns><see cref="true"/> if an object was evicted, <see cref="false"/> otherwise.</returns>
        internal bool EvictOne()
        {
            FunctionDataCacheKey cacheKey = GetFromFrontOfLRU();
            if (cacheKey == null)
            {
                return false;
            }

            return TryRemove(cacheKey);
        }

        /// <summary>
        /// Evicts objects in an LRU order (with the least recently used first) such that the objects have no active references,
        /// until either the specified capacity has been made available or cannot be made available.
        /// Note: Assumes that it is called in a thread-safe manner (i.e. <see cref="_lock"/> is held
        /// by the caller.)
        /// </summary>
        /// <param name="capacityRequiredBytes">Capacity (in number of bytes) required.</param>
        /// <returns><see cref="true"/> if the required capacity has been made available, <see cref="false"/> otherwise.</returns>
        internal bool EvictUntilCapacityAvailable(long capacityRequiredBytes)
        {
            if (capacityRequiredBytes > _maximumCapacityBytes)
            {
                return false;
            }

            while (RemainingCapacityBytes < capacityRequiredBytes)
            {
                if (!EvictOne())
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Obtains the maximum capacity that the cache can have from the passed environment.
        /// If the environment does not have a value specified then a default value of
        /// <see cref="FunctionDataCacheConstants.FunctionDataCacheDefaultMaximumSizeBytes"/> is used.
        /// </summary>
        /// <param name="environment">Environment from which to get the configuration from.</param>
        /// <returns>Number of bytes of capacity that the cache can have.</returns>
        private long GetMaximumCapacityBytes(IEnvironment environment)
        {
            string capacityVal = environment.GetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheMaximumSizeBytesSettingName);
            if (!string.IsNullOrEmpty(capacityVal) &&
                long.TryParse(capacityVal, out long capacity) &&
                capacity > 0)
            {
                return capacity;
            }

            return FunctionDataCacheConstants.FunctionDataCacheDefaultMaximumSizeBytes;
        }

        /// <summary>
        /// Checks the passed environment to see if the cache is to be enabled or not.
        /// Looks for the value specified for <see cref="FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName"/>.
        /// If a value is not specified, the cache is disabled by default.
        /// </summary>
        /// <param name="environment">Environment from which to get the configuration from.</param>
        /// <returns><see cref="true"/> if the cache is to be enabled, <see cref="false"/> otherwise.</returns>
        private bool GetIsEnabled(IEnvironment environment)
        {
            // Check if the environment variable (AppSetting) has this feature enabled
            string envVal = environment.GetEnvironmentVariable(FunctionDataCacheConstants.FunctionDataCacheEnabledSettingName);
            if (string.IsNullOrEmpty(envVal))
            {
                return false;
            }

            bool envValEnabled = false;
            if (bool.TryParse(envVal, out bool boolResult))
            {
                // Check if value was specified as a bool (true/false)
                envValEnabled = boolResult;
            }
            else if (int.TryParse(envVal, out int intResult) && intResult == 1)
            {
                // Check if value was specified as an int (1/0)
                envValEnabled = true;
            }

            return envValEnabled;
        }

        /// <summary>
        /// Increments the active reference count for the given key.
        /// Note: Assumes that it is called in a thread-safe manner (i.e. <see cref="_lock"/> is held
        /// by the caller.)
        /// </summary>
        /// <param name="cacheKey">Key for which to increment the active reference count.</param>
        private void IncrementActiveReference(FunctionDataCacheKey cacheKey)
        {
            long activeReferences = 0;
            if (ActiveReferences.TryGetValue(cacheKey, out activeReferences))
            {
                ActiveReferences.Remove(cacheKey);
            }
            ActiveReferences.Add(cacheKey, activeReferences + 1);
        }

        /// <summary>
        /// Decrements the active reference count for the given key.
        /// Note: Assumes that it is called in a thread-safe manner (i.e. <see cref="_lock"/> is held
        /// by the caller.)
        /// </summary>
        /// <param name="cacheKey">Key for which to decrement the active reference count.</param>
        private void DecrementActiveReferenceCore(FunctionDataCacheKey cacheKey)
        {
            long activeReferences = 0;
            if (ActiveReferences.TryGetValue(cacheKey, out activeReferences))
            {
                ActiveReferences.Remove(cacheKey);
                if (activeReferences > 1)
                {
                    ActiveReferences.Add(cacheKey, activeReferences - 1);
                }
            }
        }

        /// <summary>
        /// Adds the given key to the end of the LRU list.
        /// This key is the most recently used key.
        /// Note: Assumes that it is called in a thread-safe manner (i.e. <see cref="_lock"/> is held
        /// by the caller.)
        /// </summary>
        /// <param name="cacheKey">Key to add to the end of the LRU list.</param>
        private void AddToEndOfLRU(FunctionDataCacheKey cacheKey)
        {
            if (LRUList.Contains(cacheKey))
            {
                RemoveFromLRU(cacheKey);
            }

            LRUList.AddLast(cacheKey);
        }

        /// <summary>
        /// Gets the first key (least recently used) starting from the front of the LRU list, such that the key does not have any active references.
        /// Note: Assumes that it is called in a thread-safe manner (i.e. <see cref="_lock"/> is held
        /// by the caller.)
        /// </summary>
        /// <returns>The first <see cref="FunctionDataCacheKey"/> from the front of the list which does not have any active references,
        /// or <see cref="null"/> if no such valid key is found.</returns>
        private FunctionDataCacheKey GetFromFrontOfLRU()
        {
            if (LRUList.Count == 0)
            {
                return null;
            }

            foreach (FunctionDataCacheKey cacheKey in LRUList)
            {
                if (!ActiveReferences.ContainsKey(cacheKey))
                {
                    return cacheKey;
                }
            }

            return null;
        }

        /// <summary>
        /// Removes the given key from the LRU list, if it exists.
        /// Note: Assumes that it is called in a thread-safe manner (i.e. <see cref="_lock"/> is held
        /// by the caller.)
        /// </summary>
        /// <param name="cacheKey">Key to remove from the LRU list.</param>
        private void RemoveFromLRU(FunctionDataCacheKey cacheKey)
        {
            LRUList.Remove(cacheKey);
        }
    }
}
