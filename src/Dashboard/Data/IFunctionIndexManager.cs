// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public interface IFunctionIndexManager
    {
        IEnumerable<VersionedMetadata> List(string hostId);

        bool CreateOrUpdateIfLatest(FunctionSnapshot snapshot);

        bool UpdateOrCreateIfLatest(FunctionSnapshot snapshot, string currentETag, DateTimeOffset currentVersion);

        bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion);

        bool DeleteIfLatest(string id, DateTimeOffset deleteThroughVersion, string currentETag,
            DateTimeOffset currentVersion);
    }
}
