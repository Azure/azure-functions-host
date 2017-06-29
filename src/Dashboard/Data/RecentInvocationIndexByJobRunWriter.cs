// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByJobRunWriter : IRecentInvocationIndexByJobRunWriter
    {
        private readonly IConcurrentMetadataTextStore _store;
        
        public RecentInvocationIndexByJobRunWriter(CloudBlobClient client)
            : this(ConcurrentTextStore.CreateBlobStore(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.RecentFunctionsByJobRun))
        {
        }

        private RecentInvocationIndexByJobRunWriter(IConcurrentMetadataTextStore store)
        {
            _store = store;
        }

        public void CreateOrUpdate(FunctionInstanceSnapshot snapshot, WebJobRunIdentifier webJobRunId, DateTimeOffset timestamp)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            string innerId = CreateInnerId(webJobRunId, timestamp, snapshot.Id);
            _store.CreateOrUpdate(innerId, RecentInvocationEntry.CreateMetadata(snapshot), String.Empty);
        }

        public void DeleteIfExists(WebJobRunIdentifier webJobRunId, DateTimeOffset timestamp, Guid id)
        {
            string innerId = CreateInnerId(webJobRunId, timestamp, id);
            _store.DeleteIfExists(innerId);
        }

        private static string CreateInnerId(WebJobRunIdentifier webJobRunId, DateTimeOffset timestamp, Guid id)
        {
            return DashboardBlobPrefixes.CreateByJobRunRelativePrefix(webJobRunId) +
                RecentInvocationEntry.CreateBlobName(timestamp, id);
        }
    }
}
