// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexWriter
    {
        void CreateOrUpdate(FunctionInstanceSnapshot snapshot, DateTimeOffset timestamp);

        void DeleteIfExists(DateTimeOffset timestamp, Guid id);
    }
}
