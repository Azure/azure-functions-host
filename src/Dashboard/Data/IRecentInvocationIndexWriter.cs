// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexWriter
    {
        void CreateOrUpdate(FunctionInstanceSnapshot snapshot, DateTimeOffset timestamp);

        void DeleteIfExists(DateTimeOffset timestamp, Guid id);
    }
}
