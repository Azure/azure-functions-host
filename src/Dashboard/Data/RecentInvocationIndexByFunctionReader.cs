// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByFunctionReader : IRecentInvocationIndexByFunctionReader
    {
        private readonly IBlobRecentInvocationIndexReader _innerReader;

        [CLSCompliant(false)]
        public RecentInvocationIndexByFunctionReader(CloudBlobClient client)
            : this (new BlobRecentInvocationIndexReader(client, DashboardDirectoryNames.RecentFunctionsByFunction))
        {
        }

        private RecentInvocationIndexByFunctionReader(IBlobRecentInvocationIndexReader innerReader)
        {
            _innerReader = innerReader;
        }

        public IResultSegment<RecentInvocationEntry> Read(string functionId, int maximumResults,
            string continuationToken)
        {
            string relativePrefix = DashboardBlobPrefixes.CreateByFunctionRelativePrefix(functionId);
            return _innerReader.Read(relativePrefix, maximumResults, continuationToken);
        }
    }
}
