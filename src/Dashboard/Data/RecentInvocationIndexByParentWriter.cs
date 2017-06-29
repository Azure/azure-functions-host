// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByParentWriter : IRecentInvocationIndexByParentWriter
    {
        private readonly IConcurrentMetadataTextStore _store;
        
        public RecentInvocationIndexByParentWriter(CloudBlobClient client)
            : this(ConcurrentTextStore.CreateBlobStore(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.RecentFunctionsByParent))
        {
        }

        private RecentInvocationIndexByParentWriter(IConcurrentMetadataTextStore store)
        {
            _store = store;
        }

        public void CreateOrUpdate(FunctionInstanceSnapshot snapshot, DateTimeOffset timestamp)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            string innerId = CreateInnerId(snapshot.ParentId.Value, timestamp, snapshot.Id);
            _store.CreateOrUpdate(innerId, RecentInvocationEntry.CreateMetadata(snapshot), String.Empty);
        }

        public void DeleteIfExists(Guid parentId, DateTimeOffset timestamp, Guid id)
        {
            string innerId = CreateInnerId(parentId, timestamp, id);
            _store.DeleteIfExists(innerId);
        }

        private static string CreateInnerId(Guid parentId, DateTimeOffset timestamp, Guid id)
        {
            return DashboardBlobPrefixes.CreateByParentRelativePrefix(parentId) +
                RecentInvocationEntry.CreateBlobName(timestamp, id);
        }
    }
}
