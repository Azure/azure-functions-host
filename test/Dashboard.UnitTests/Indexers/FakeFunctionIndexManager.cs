// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dashboard.Data;

namespace Dashboard.UnitTests.Indexers
{
    internal class FakeFunctionIndexManager : IFunctionIndexManager
    {
        private readonly IList<StoreItem> _store = new List<StoreItem>();

        public IEnumerable<VersionedMetadata> List(string hostId)
        {
            IEnumerable<StoreItem> matches = _store.Where(i => i.FunctionSnapshot.QueueName == hostId)
                .OrderBy(i => i.FunctionSnapshot.HostFunctionId);
            List<VersionedMetadata> results = new List<VersionedMetadata>();

            foreach (StoreItem item in matches)
            {
                IDictionary<string, string> metadata = new Dictionary<string, string>();
                results.Add(new VersionedMetadata(item.FunctionSnapshot.HostFunctionId, item.ETag, metadata,
                    item.FunctionSnapshot.HostVersion));
            }

            return results;
        }

        public bool CreateOrUpdateIfLatest(FunctionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            StoreItem existing = _store.Where(i => i.FunctionSnapshot.Id == snapshot.Id).FirstOrDefault();

            if (existing == null)
            {
                _store.Add(CreateStoreItem(snapshot));
                return true;
            }
            else if (existing.FunctionSnapshot.HostVersion < snapshot.HostVersion)
            {
                _store.Remove(existing);
                _store.Add(CreateStoreItem(snapshot));
                return true;
            }
            else
            {
                // Tell callers when they have the latest version, even if it was already present.
                return existing.FunctionSnapshot.HostVersion == snapshot.HostVersion;
            }
        }

        public bool UpdateOrCreateIfLatest(FunctionSnapshot snapshot, string currentETag, DateTimeOffset currentVersion)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            StoreItem existing = _store.Where(i => i.FunctionSnapshot.Id == snapshot.Id).FirstOrDefault();

            if (existing == null)
            {
                _store.Add(CreateStoreItem(snapshot));
                return true;
            }
            else if (currentETag == existing.ETag && currentVersion < snapshot.HostVersion)
            {
                Debug.Assert(currentVersion == existing.FunctionSnapshot.HostVersion);
                _store.Remove(existing);
                _store.Add(CreateStoreItem(snapshot));
                return true;
            }
            else
            {
                // Tell callers when they have the latest version, even if it was already present.
                return existing.FunctionSnapshot.HostVersion == snapshot.HostVersion;
            }
        }

        public bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion)
        {
            StoreItem existing = _store.Where(i => i.FunctionSnapshot.Id == id).FirstOrDefault();

            if (existing == null)
            {
                return true;
            }

            if (existing.FunctionSnapshot.HostVersion <= deleteThroughVersion)
            {
                _store.Remove(existing);
                return true;
            }

            return false;
        }

        public bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion, string currentETag, DateTimeOffset currentVersion)
        {
            StoreItem existing = _store.Where(i => i.FunctionSnapshot.Id == id).FirstOrDefault();

            if (existing == null)
            {
                return true;
            }

            if (currentETag == existing.ETag && existing.FunctionSnapshot.HostVersion <= deleteThroughVersion)
            {
                Debug.Assert(currentVersion == existing.FunctionSnapshot.HostVersion);
                _store.Remove(existing);
                return true;
            }

            return false;
        }

        private static StoreItem CreateStoreItem(FunctionSnapshot snapshot)
        {
            return new StoreItem
            {
                FunctionSnapshot = snapshot,
                ETag = Guid.NewGuid().ToString()
            };
        }

        private class StoreItem
        {
            public FunctionSnapshot FunctionSnapshot { get; set; }
            public string ETag { get; set; }
        }
    }
}
