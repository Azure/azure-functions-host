// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexReader
    {
        IResultSegment<RecentInvocationEntry> Read(int maximumResults, string continuationToken);
    }
}
