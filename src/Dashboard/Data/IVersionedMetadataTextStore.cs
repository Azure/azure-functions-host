// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public interface IVersionedMetadataTextStore
    {
        IEnumerable<VersionedMetadata> List(string prefix);

        VersionedMetadataText Read(string id);

        bool CreateOrUpdateIfLatest(string id, DateTimeOffset targetVersion, IDictionary<string, string> otherMetadata,
            string text);

        bool UpdateOrCreateIfLatest(string id, DateTimeOffset targetVersion, IDictionary<string, string> otherMetadata,
            string text);

        bool UpdateOrCreateIfLatest(string id, DateTimeOffset targetVersion, IDictionary<string, string> otherMetadata,
            string text, string currentETag, DateTimeOffset currentVersion);

        bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion);

        bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion, string currentETag,
            DateTimeOffset currentVersion);
    }
}
