// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexReader : IRecentInvocationIndexReader
    {
        private readonly IBlobRecentInvocationIndexReader _innerReader;
        
        public RecentInvocationIndexReader(CloudBlobClient client)
            : this(new BlobRecentInvocationIndexReader(client, DashboardDirectoryNames.RecentFunctionsFlat))
        {
        }

        private RecentInvocationIndexReader(IBlobRecentInvocationIndexReader innerReader)
        {
            _innerReader = innerReader;
        }

        public IResultSegment<RecentInvocationEntry> Read(int maximumResults, string continuationToken)
        {
            return _innerReader.Read(String.Empty, maximumResults, continuationToken);
        }
    }
}
