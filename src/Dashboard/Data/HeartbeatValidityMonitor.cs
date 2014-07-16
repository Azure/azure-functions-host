// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Web.Caching;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    /// <summary>Defines a monitor for valid running host heartbeats.</summary>
    public class HeartbeatValidityMonitor : IHeartbeatValidityMonitor
    {
        private readonly IHeartbeatMonitor _innerMonitor;
        private readonly Cache _cache;

        public HeartbeatValidityMonitor(IHeartbeatMonitor innerMonitor, Cache cache)
        {
            _innerMonitor = innerMonitor;
            _cache = cache;
        }

        /// <inheritdoc />
        public bool IsSharedHeartbeatValid(string sharedContainerName, string sharedDirectoryName,
            int expirationInSeconds)
        {
            string cacheKey = null;

            if (_cache != null)
            {
                cacheKey = GetSharedCacheKey(sharedContainerName, sharedDirectoryName, expirationInSeconds);
                object cachedValue = _cache.Get(cacheKey);

                // The value isn't significant; the presence of an unexpired cache item is all we need to check.
                if (cachedValue != null)
                {
                    return true;
                }
            }

            DateTimeOffset? validExpiration = _innerMonitor.GetSharedHeartbeatExpiration(sharedContainerName,
                sharedDirectoryName, expirationInSeconds);

            if (!validExpiration.HasValue)
            {
                return false;
            }

            // Any non-null value has already been validate as unexpired. (Re-validating could actually introduce
            // incorrect results, as time has moved, and we only looked for the first heartbeat that wasn't expired at
            // the time.)
            DateTime expirationUtc = validExpiration.Value.UtcDateTime;

            if (_cache != null)
            {
                _cache.Insert(cacheKey, true, dependencies: null, absoluteExpiration: expirationUtc,
                    slidingExpiration: Cache.NoSlidingExpiration);
            }

            return true;
        }

        /// <inheritdoc />
        public bool IsInstanceHeartbeatValid(string sharedContainerName, string sharedDirectoryName,
            string instanceBlobName, int expirationInSeconds)
        {
            string cacheKey = null;

            if (_cache != null)
            {
                cacheKey = GetInstanceCacheKey(sharedContainerName, sharedDirectoryName, instanceBlobName,
                    expirationInSeconds);

                object cachedValue = _cache.Get(cacheKey);

                // The value isn't significant; the presence of an unexpired cache item is all we need to check.
                if (cachedValue != null)
                {
                    return true;
                }
            }

            DateTimeOffset? validExpiration = _innerMonitor.GetInstanceHeartbeatExpiration(sharedContainerName,
                sharedDirectoryName, instanceBlobName, expirationInSeconds);

            if (!validExpiration.HasValue)
            {
                return false;
            }

            // Any non-null value has already been validate as unexpired.
            DateTime expirationUtc = validExpiration.Value.UtcDateTime;

            if (_cache != null)
            {
                _cache.Insert(cacheKey, true, dependencies: null, absoluteExpiration: expirationUtc,
                    slidingExpiration: Cache.NoSlidingExpiration);
            }

            return true;
        }

        private static string GetSharedCacheKey(string sharedContainerName, string sharedDirectoryName,
            int expirationInSeconds)
        {
            return String.Format(CultureInfo.InvariantCulture, "SHARED_HEARTBEAT_{0}_{1}_{2}", sharedContainerName,
                sharedDirectoryName, expirationInSeconds);
        }

        private static string GetInstanceCacheKey(string sharedContainerName, string sharedDirectoryName,
            string instanceBlobName, int expirationInSeconds)
        {
            return String.Format(CultureInfo.InvariantCulture, "INSTANCE_HEARTBEAT_{0}_{1}_{2}_{3}", sharedContainerName,
                sharedDirectoryName, instanceBlobName, expirationInSeconds);
        }
    }
}
