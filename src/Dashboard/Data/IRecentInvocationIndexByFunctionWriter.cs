// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexByFunctionWriter
    {
        void CreateOrUpdate(string functionId, DateTimeOffset timestamp, Guid id);

        void DeleteIfExists(string functionId, DateTimeOffset timestamp, Guid id);
    }
}
