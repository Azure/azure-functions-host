// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexByParentReader
    {
        IResultSegment<RecentInvocationEntry> Read(Guid parentId, int maximumResults, string continuationToken);
    }
}
