// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    internal class NullInvocationIndexReader : IRecentInvocationIndexByJobRunReader, IRecentInvocationIndexByParentReader
    {
        IResultSegment<RecentInvocationEntry> IRecentInvocationIndexByParentReader.Read(Guid parentId, int maximumResults, string continuationToken)
        {
            var x = new RecentInvocationEntry[0];
            return new ResultSegment<RecentInvocationEntry>(x, null);
        }

        IResultSegment<RecentInvocationEntry> IRecentInvocationIndexByJobRunReader.Read(WebJobRunIdentifier webJobRunId, int maximumResults, string continuationToken)
        {
            var x = new RecentInvocationEntry[0];
            return new ResultSegment<RecentInvocationEntry>(x, null);
        }
    }
}