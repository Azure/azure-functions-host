// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Dashboard.Data;

namespace Dashboard.UnitTests.Indexers
{
    internal class FakeHostIndexManager : IHostIndexManager
    {
        private readonly IDictionary<string, HostSnapshot> _store = new Dictionary<string, HostSnapshot>();

        public HostSnapshot Read(string id)
        {
            if (!_store.ContainsKey(id))
            {
                return null;
            }

            return _store[id];
        }

        public bool UpdateOrCreateIfLatest(string id, HostSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            if (!_store.ContainsKey(id))
            {
                _store.Add(id, snapshot);
                return true;
            }
            else
            {
                HostSnapshot existing = _store[id];

                if (snapshot.HostVersion > existing.HostVersion)
                {
                    _store[id] = snapshot;
                    return true;
                }
                else
                {
                    // Tell callers when they have the latest version, even if it was already present.
                    return snapshot.HostVersion == existing.HostVersion;
                }
            }
        }
    }
}
