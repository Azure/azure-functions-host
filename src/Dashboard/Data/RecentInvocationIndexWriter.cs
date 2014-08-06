// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class RecentInvocationIndexWriter : IRecentInvocationIndexWriter
    {
        private readonly IConcurrentMetadataTextStore _store;

        [CLSCompliant(false)]
        public RecentInvocationIndexWriter(CloudBlobClient client)
            : this(ConcurrentTextStore.CreateBlobStore(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.RecentFunctionsFlat))
        {
        }

        private RecentInvocationIndexWriter(IConcurrentMetadataTextStore store)
        {
            _store = store;
        }

        public void CreateOrUpdate(FunctionInstanceSnapshot snapshot, DateTimeOffset timestamp)
        {
            var innerId = CreateInnerId(timestamp, snapshot.Id);
            var metadata = FunctionInstanceMetadata.CreateFromSnapshot(snapshot);
            _store.CreateOrUpdate(innerId, metadata, String.Empty);
        }

        public void DeleteIfExists(DateTimeOffset timestamp, Guid id)
        {
            string innerId = CreateInnerId(timestamp, id);
            _store.DeleteIfExists(innerId);
        }

        private static string CreateInnerId(DateTimeOffset timestamp, Guid id)
        {
            return RecentInvocationEntry.Format(timestamp, id);
        }
    }
}
