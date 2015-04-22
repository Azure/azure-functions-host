// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByParentReader : IRecentInvocationIndexByParentReader
    {
        private readonly IBlobRecentInvocationIndexReader _innerReader;

        [CLSCompliant(false)]
        public RecentInvocationIndexByParentReader(CloudBlobClient client)
            : this (new BlobRecentInvocationIndexReader(client, DashboardDirectoryNames.RecentFunctionsByParent))
        {
        }

        private RecentInvocationIndexByParentReader(IBlobRecentInvocationIndexReader innerReader)
        {
            _innerReader = innerReader;
        }

        public IResultSegment<RecentInvocationEntry> Read(Guid parentId, int maximumResults, string continuationToken)
        {
            string relativePrefix = DashboardBlobPrefixes.CreateByParentRelativePrefix(parentId);
            return _innerReader.Read(relativePrefix, maximumResults, continuationToken);
        }
    }
}
