// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    public interface IRecentInvocationIndexByJobRunReader
    {
        IResultSegment<RecentInvocationEntry> Read(WebJobRunIdentifier webJobRunId, int maximumResults,
            string continuationToken);
    }
}
