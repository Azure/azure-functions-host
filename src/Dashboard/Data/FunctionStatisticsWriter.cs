// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class FunctionStatisticsWriter : IFunctionStatisticsWriter
    {
        private readonly IConcurrentDocumentStore<FunctionStatistics> _store;

        [CLSCompliant(false)]
        public FunctionStatisticsWriter(CloudBlobClient client)
            : this(ConcurrentDocumentStore.CreateJsonBlobStore<FunctionStatistics>(
                client, DashboardContainerNames.Dashboard, DashboardDirectoryNames.FunctionStatistics))
        {
        }

        private FunctionStatisticsWriter(IConcurrentDocumentStore<FunctionStatistics> store)
        {
            _store = store;
        }

        public void IncrementSuccess(string functionId)
        {
            UpdateEntity(functionId, (e) => e.SucceededCount++);
        }

        public void IncrementFailure(string functionId)
        {
            UpdateEntity(functionId, (e) => e.FailedCount++);
        }

        private void UpdateEntity(string functionId, Action<FunctionStatistics> modifier)
        {
            // Keep racing to update the entity until it succeeds.
            while (!TryUpdateEntity(functionId, modifier))
            {
            }
        }

        private bool TryUpdateEntity(string functionId, Action<FunctionStatistics> modifier)
        {
            IConcurrentDocument<FunctionStatistics> result = _store.Read(functionId);

            if (result == null || result.Document == null)
            {
                FunctionStatistics statistics = new FunctionStatistics();
                modifier.Invoke(statistics);
                return _store.TryCreate(functionId, statistics);
            }
            else
            {
                FunctionStatistics statistics = result.Document;
                modifier.Invoke(statistics);
                return _store.TryUpdate(functionId, result.ETag, statistics);
            }
        }
    }
}
