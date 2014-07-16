// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByJobRunReader : IRecentInvocationIndexByJobRunReader
    {
        private readonly IBlobRecentInvocationIndexReader _innerReader;

        [CLSCompliant(false)]
        public RecentInvocationIndexByJobRunReader(CloudBlobClient client)
            : this (new BlobRecentInvocationIndexReader(client, DashboardDirectoryNames.RecentFunctionsByJobRun))
        {
        }

        private RecentInvocationIndexByJobRunReader(IBlobRecentInvocationIndexReader innerReader)
        {
            _innerReader = innerReader;
        }

        public IResultSegment<RecentInvocationEntry> Read(WebJobRunIdentifier webJobRunId, int maximumResults,
            string continuationToken)
        {
            string relativePrefix = DashboardBlobPrefixes.CreateByJobRunRelativePrefix(webJobRunId);
            return _innerReader.Read(relativePrefix, maximumResults, continuationToken);
        }
    }
}
