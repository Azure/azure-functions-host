// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexByFunctionWriter : IRecentInvocationIndexByFunctionWriter
    {
        private readonly IConcurrentMetadataTextStore _store;
        
        public RecentInvocationIndexByFunctionWriter(CloudBlobClient client)
            : this(ConcurrentTextStore.CreateBlobStore(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.RecentFunctionsByFunction))
        {
        }

        private RecentInvocationIndexByFunctionWriter(IConcurrentMetadataTextStore store)
        {
            _store = store;
        }

        public void CreateOrUpdate(FunctionInstanceSnapshot snapshot, DateTimeOffset timestamp)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            string innerId = CreateInnerId(snapshot.FunctionId, timestamp, snapshot.Id);
            _store.CreateOrUpdate(innerId, RecentInvocationEntry.CreateMetadata(snapshot), String.Empty);
        }

        public void DeleteIfExists(string functionId, DateTimeOffset timestamp, Guid id)
        {
            string innerId = CreateInnerId(functionId, timestamp, id);
            _store.DeleteIfExists(innerId);
        }

        private static string CreateInnerId(string functionId, DateTimeOffset timestamp, Guid id)
        {
            return DashboardBlobPrefixes.CreateByFunctionRelativePrefix(functionId) +
                RecentInvocationEntry.CreateBlobName(timestamp, id);
        }
    }
}
