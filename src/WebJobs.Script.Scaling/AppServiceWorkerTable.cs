// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    public sealed class AppServiceWorkerTable : IWorkerTable
    {
        public const string AppServiceWorkerTableName = "appserviceworkertable";

        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes", Justification = "By design")]
        public static readonly AppServiceWorkerTable Instance = new AppServiceWorkerTable();

        private CloudTable _workerTable;
        private CloudBlockBlob _blobLease;

        private AppServiceWorkerTable()
        {
        }

        public async Task<ILockHandle> AcquireLock()
        {
            var blobLease = await EnsureBlobLease();

            try
            {
                // optimistic that we will be able to finish in one minutes
                var id = await blobLease.AcquireLeaseAsync(TimeSpan.FromMinutes(1), AppServiceSettings.LockHandleId);
                return new AzureWorkerTableLock(blobLease, id);
            }
            catch (StorageException ex)
            {
                var webException = ex.InnerException as WebException;
                if (webException != null)
                {
                    var response = webException.Response as HttpWebResponse;
                    if (response != null)
                    {
                        throw new InvalidOperationException(string.Format("Unable to acquire worker table lock due to {0} {1}.", response.StatusCode, response.StatusDescription));
                    }
                }

                throw;
            }
        }

        public async Task AddOrUpdate(IWorkerInfo worker)
        {
            var table = await GetWorkerCloudTable();

            var entity = (AppServiceWorkerInfo)worker;
            entity.ETag = "*";

            var operation = TableOperation.InsertOrReplace(entity);
            await table.ExecuteAsync(operation);
        }

        public async Task Delete(IWorkerInfo worker)
        {
            var table = await GetWorkerCloudTable();

            var entity = (AppServiceWorkerInfo)worker;
            entity.ETag = "*";

            try
            {
                var operation = TableOperation.Delete(entity);
                await table.ExecuteAsync(operation);
            }
            catch (StorageException ex)
            {
                var webException = ex.InnerException as WebException;
                if (webException != null)
                {
                    var response = webException.Response as HttpWebResponse;
                    if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return;
                    }
                }

                throw;
            }
        }

        public async Task<IEnumerable<IWorkerInfo>> List()
        {
            var query = new TableQuery<AppServiceWorkerInfo>();
            query.Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, AppServiceSettings.WorkerPartitionKey));

            var table = await GetWorkerCloudTable();
            var results = await ExecuteQueryAsync(table, query);
            return results.OfType<IWorkerInfo>();
        }

        public async Task<IWorkerInfo> GetManager()
        {
            var table = await GetWorkerCloudTable();

            // read manager row
            var operation = TableOperation.Retrieve<AppServiceWorkerInfo>(AppServiceSettings.ManagerPartitionKey, AppServiceSettings.ManagerRowKey);
            var result = await table.ExecuteAsync(operation);
            var manager = result.Result as IWorkerInfo;
            if (manager != null)
            {
                // read from worker table
                var partitionKey = AppServiceSettings.WorkerPartitionKey;
                var rowKey = AppServiceSettings.GetWorkerRowKey(manager.StampName, manager.WorkerName);
                operation = TableOperation.Retrieve<AppServiceWorkerInfo>(partitionKey, rowKey);
                result = await table.ExecuteAsync(operation);
                manager = result.Result as IWorkerInfo;
            }

            return manager;
        }

        public async Task SetManager(IWorkerInfo worker)
        {
            // update manager row
            var entity = new AppServiceWorkerInfo
            {
                PartitionKey = AppServiceSettings.ManagerPartitionKey,
                RowKey = AppServiceSettings.ManagerRowKey,
                StampName = worker.StampName,
                WorkerName = worker.WorkerName,
                ETag = "*"
            };

            var operation = TableOperation.InsertOrReplace(entity);
            var table = await GetWorkerCloudTable();
            await table.ExecuteAsync(operation);
        }

        private async Task<CloudTable> GetWorkerCloudTable()
        {
            if (_workerTable == null)
            {
                var account = CloudStorageAccount.Parse(AppServiceSettings.StorageConnectionString);
                var client = account.CreateCloudTableClient();
                var table = client.GetTableReference(AppServiceWorkerTableName);
                await table.CreateIfNotExistsAsync();
                _workerTable = table;
            }

            return _workerTable;
        }

        private async Task<CloudBlockBlob> EnsureBlobLease()
        {
            if (_blobLease == null)
            {
                var account = CloudStorageAccount.Parse(AppServiceSettings.StorageConnectionString);
                var client = account.CreateCloudBlobClient();
                var container = client.GetContainerReference(AppServiceWorkerTableName);
                await container.CreateIfNotExistsAsync();
                var blobLease = container.GetBlockBlobReference(AppServiceSettings.SiteName);

                if (!await blobLease.ExistsAsync())
                {
                    var buffer = Encoding.UTF8.GetBytes(AppServiceSettings.SiteName);
                    await blobLease.UploadFromByteArrayAsync(buffer, 0, buffer.Length);
                }

                _blobLease = blobLease;
            }

            return _blobLease;
        }

        private static async Task<IEnumerable<T>> ExecuteQueryAsync<T>(CloudTable table, TableQuery<T> query) where T : ITableEntity, new()
        {
            var runningQuery = new TableQuery<T>()
            {
                FilterString = query.FilterString,
                SelectColumns = query.SelectColumns
            };

            var workers = new List<T>();
            TableContinuationToken token = null;
            do
            {
                runningQuery.TakeCount = query.TakeCount - workers.Count;
                var segments = await table.ExecuteQuerySegmentedAsync(runningQuery, token);
                workers.AddRange(segments);
                token = segments.ContinuationToken;
            }
            while (token != null);

            return workers;
        }

        private class AzureWorkerTableLock : ILockHandle
        {
            private readonly CloudBlockBlob _blobLease;
            private readonly string _id;

            public AzureWorkerTableLock(CloudBlockBlob blobLease, string id)
            {
                _blobLease = blobLease;
                _id = id;
            }

            public string Id
            {
                get { return _id; }
            }

            public async Task Release()
            {
                await _blobLease.ReleaseLeaseAsync(new AccessCondition { LeaseId = _id });
            }
        }
    }
}