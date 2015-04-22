// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class VersionedMetadataTextStore : IVersionedMetadataTextStore
    {
        private readonly IConcurrentMetadataTextStore _innerStore;
        private readonly IVersionMetadataMapper _versionMapper;

        public VersionedMetadataTextStore(IConcurrentMetadataTextStore innerStore, IVersionMetadataMapper versionMapper)
        {
            if (innerStore == null)
            {
                throw new ArgumentNullException("innerStore");
            }
            else if (versionMapper == null)
            {
                throw new ArgumentNullException("versionMapper");
            }

            _innerStore = innerStore;
            _versionMapper = versionMapper;
        }

        public IEnumerable<VersionedMetadata> List(string prefix)
        {
            IEnumerable<ConcurrentMetadata> innerResults = _innerStore.List(prefix);

            if (innerResults == null)
            {
                return null;
            }

            List<VersionedMetadata> results = new List<VersionedMetadata>();

            foreach (ConcurrentMetadata innerResult in innerResults)
            {
                IDictionary<string, string> metadata = innerResult.Metadata;
                DateTimeOffset version = _versionMapper.GetVersion(metadata);
                VersionedMetadata result = new VersionedMetadata(innerResult.Id, innerResult.ETag, metadata, version);
                results.Add(result);
            }

            return results;
        }

        public VersionedMetadataText Read(string id)
        {
            ConcurrentMetadataText innerItem = _innerStore.Read(id);

            if (innerItem == null)
            {
                return null;
            }

            DateTimeOffset version = GetVersion(innerItem.Metadata);
            return new VersionedMetadataText(innerItem.ETag, innerItem.Metadata, version, innerItem.Text);
        }

        public bool CreateOrUpdateIfLatest(string id, DateTimeOffset targetVersion,
            IDictionary<string, string> otherMetadata, string text)
        {
            return PersistIfLatest(id, targetVersion, otherMetadata, text, startingItem: null);
        }

        public bool UpdateOrCreateIfLatest(string id, DateTimeOffset targetVersion,
            IDictionary<string, string> otherMetadata, string text)
        {
            VersionedItem startingItem = ReadMetadata(id);
            return PersistIfLatest(id, targetVersion, otherMetadata, text, startingItem);
        }

        public bool UpdateOrCreateIfLatest(string id, DateTimeOffset targetVersion,
            IDictionary<string, string> otherMetadata, string text, string currentETag, DateTimeOffset currentVersion)
        {
            return PersistIfLatest(id, targetVersion, otherMetadata, text,
                startingItem: new VersionedItem(currentETag, currentVersion));
        }

        public bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion)
        {
            VersionedItem startingItem = ReadMetadata(id);
            return DeleteIfLatest(id, deleteThroughVersion, startingItem);
        }

        public bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion, string currentETag,
            DateTimeOffset currentVersion)
        {
            return DeleteIfLatest(id, deleteThroughVersion,
                startingItem: new VersionedItem(currentETag, currentVersion));
        }

        private bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion, VersionedItem startingItem)
        {
            bool deleted = false;
            string previousETag = null;
            VersionedItem currentItem;

            for (currentItem = startingItem;
                !deleted && currentItem != null && currentItem.Version <= deleteThroughVersion;
                currentItem = ReadMetadata(id, deleted))
            {
                string currentETag = currentItem.ETag;

                // Prevent an infinite loop if _innerStore erroneously returns false from TryDelete when a retry won't
                // help. (The inner store should throw rather than return false in that case.)
                if (currentETag == previousETag)
                {
                    throw new InvalidOperationException("The operation stopped making progress.");
                }

                previousETag = currentETag;
                deleted = _innerStore.TryDelete(id, currentETag);
            }

            // Compare the condition of the loop above. The caller's version is the latest if we return for any reason
            // other than that a newer version now exists.
            // If we succesfully deleted the item or someone else did, the caller's version is the latest.
            return deleted || currentItem == null;
        }

        private IDictionary<string, string> Combine(IDictionary<string, string> otherMetadata, DateTimeOffset version)
        {
            IDictionary<string, string> metadata;

            if (otherMetadata != null)
            {
                metadata = otherMetadata;
            }
            else
            {
                metadata = new Dictionary<string, string>();
            }

            _versionMapper.SetVersion(version, metadata);
            return metadata;
        }

        private DateTimeOffset GetVersion(ConcurrentMetadataText item)
        {
            if (item == null)
            {
                return DateTimeOffset.MinValue;
            }

            return GetVersion(item.Metadata);
        }

        private DateTimeOffset GetVersion(IDictionary<string, string> metadata)
        {
            if (metadata == null)
            {
                return DateTimeOffset.MinValue;
            }

            return _versionMapper.GetVersion(metadata);
        }

        private DateTimeOffset GetVersion(VersionedItem item)
        {
            if (item == null)
            {
                return DateTimeOffset.MinValue;
            }

            return item.Version;
        }

        private bool PersistIfLatest(string id, DateTimeOffset targetVersion, IDictionary<string, string> otherMetadata,
            string text, VersionedItem startingItem)
        {
            if (targetVersion == DateTimeOffset.MinValue)
            {
                // DateTimeOffset.MinValue is a sentinal value used by the implementation, so it can't be used as a
                // targetVersion.
                throw new ArgumentException("targetVersion must be greater than DateTimeOffset.MinValue.",
                    "targetVersion");
            }

            IDictionary<string, string> combinedMetadata = Combine(otherMetadata, targetVersion);
            bool persisted = false;
            string previousETag = null;
            int createAttempt = 0;
            const int maximumCreateAttempts = 10;
            VersionedItem currentItem;

            for (currentItem = startingItem;
                !persisted && GetVersion(currentItem) < targetVersion;
                currentItem = ReadMetadata(id, persisted))
            {
                if (currentItem == null)
                {
                    if (createAttempt++ >= maximumCreateAttempts)
                    {
                        // Prevent an infinite loop if _innerStore erroneously returns false from TryCreate when a retry
                        // won't help. (The inner store should throw rather than return false in that case.)
                        // Note that there's no reliable, immediate way to distinguish this error from the unlikely but
                        // possible case of a series of decreasily old items that keep getting created and then deleted.
                        throw new InvalidOperationException(
                            "The operation gave up due to repeated failed creation attempts.");
                    }

                    previousETag = null;
                    persisted = _innerStore.TryCreate(id, combinedMetadata, text);
                }
                else
                {
                    createAttempt = 0;
                    string currentETag = currentItem.ETag;

                    // Prevent an infinite loop if _innerStore erroneously returns false from TryUpdate when a retry
                    // won't help. (The inner store should throw rather than return false in that case.)
                    if (currentETag == previousETag)
                    {
                        throw new InvalidOperationException("The operation stopped making progress.");
                    }

                    previousETag = currentETag;
                    persisted = _innerStore.TryUpdate(id, currentETag, combinedMetadata, text);
                }
            }

            // Compare the condition of the loop above. The caller's version is the latest if we return for any reason
            // other than that a newer version now exists.
            // If we succesfully updated the item to the target version or someone else did, the caller's version is the
            // latest.
            return persisted || GetVersion(currentItem) == targetVersion;
        }

        private VersionedItem ReadMetadata(string id)
        {
            ConcurrentMetadata innerItem = _innerStore.ReadMetadata(id);

            if (innerItem == null)
            {
                return null;
            }

            return new VersionedItem(innerItem.ETag, GetVersion(innerItem.Metadata));
        }

        private VersionedItem ReadMetadata(string id, bool shortCircuit)
        {
            if (shortCircuit)
            {
                return null;
            }

            return ReadMetadata(id);
        }

        private class VersionedItem
        {
            private readonly string _eTag;
            private readonly DateTimeOffset _version;

            public VersionedItem(string eTag, DateTimeOffset version)
            {
                _eTag = eTag;
                _version = version;
            }

            public string ETag { get { return _eTag; } }
            public DateTimeOffset Version { get { return _version; } }
        }
    }
}
