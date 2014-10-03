// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Represents a table.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageTable : IStorageTable
#else
    internal class StorageTable : IStorageTable
#endif
    {
        private readonly CloudTable _sdk;

        /// <summary>Initializes a new instance of the <see cref="StorageTable"/> class.</summary>
        /// <param name="sdk">The SDK table to wrap.</param>
        public StorageTable(CloudTable sdk)
        {
            _sdk = sdk;
        }

        /// <inheritdoc />
        public string Name
        {
            get { return _sdk.Name; }
        }

        /// <inheritdoc />
        public CloudTable SdkObject
        {
            get { return _sdk; }
        }

        /// <inheritdoc />
        public IStorageTableBatchOperation CreateBatch()
        {
            TableBatchOperation sdkBatch = new TableBatchOperation();
            return new StorageTableBatchOperation(sdkBatch);
        }

        /// <inheritdoc />
        public Task CreateIfNotExistsAsync(CancellationToken cancellationToken)
        {
            return _sdk.CreateIfNotExistsAsync(cancellationToken);
        }

        /// <inheritdoc />
        public IStorageTableOperation CreateInsertOperation(ITableEntity entity)
        {
            return StorageTableOperation.Insert(entity);
        }

        /// <inheritdoc />
        public IStorageTableOperation CreateInsertOrReplaceOperation(ITableEntity entity)
        {
            return StorageTableOperation.InsertOrReplace(entity);
        }

        /// <inheritdoc />
        public IQueryable<TElement> CreateQuery<TElement>() where TElement : ITableEntity, new()
        {
            return _sdk.CreateQuery<TElement>();
        }

        /// <inheritdoc />
        public IStorageTableOperation CreateReplaceOperation(ITableEntity entity)
        {
            return StorageTableOperation.Replace(entity);
        }

        /// <inheritdoc />
        public IStorageTableOperation CreateRetrieveOperation<TElement>(string partitionKey, string rowKey)
            where TElement : ITableEntity, new()
        {
            return StorageTableOperation.Retrieve<TElement>(partitionKey, rowKey);
        }

        /// <inheritdoc />
        public Task<TableResult> ExecuteAsync(IStorageTableOperation operation, CancellationToken cancellationToken)
        {
            TableOperation sdkOperation = ((StorageTableOperation)operation).SdkObject;
            return _sdk.ExecuteAsync(sdkOperation, cancellationToken);
        }

        /// <inheritdoc />
        public Task<IList<TableResult>> ExecuteBatchAsync(IStorageTableBatchOperation batch,
            CancellationToken cancellationToken)
        {
            TableBatchOperation sdkBatch = ((StorageTableBatchOperation)batch).SdkObject;
            return _sdk.ExecuteBatchAsync(sdkBatch, cancellationToken);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return _sdk.ExistsAsync(cancellationToken);
        }
    }
}
