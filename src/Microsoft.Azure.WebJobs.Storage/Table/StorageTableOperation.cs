// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Represents an operation on a table.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageTableOperation : IStorageTableOperation
#else
    internal class StorageTableOperation : IStorageTableOperation
#endif
    {
        private readonly TableOperation _sdk;
        private readonly TableOperationType _operationType;
        private readonly ITableEntity _entity;
        private readonly string _retrievePartitionKey;
        private readonly string _retrieveRowKey;
        private readonly IEntityResolver _retrieveEntityResolver;

        private StorageTableOperation(TableOperation sdk, TableOperationType operationType, ITableEntity entity)
        {
            _sdk = sdk;
            _operationType = operationType;
            _entity = entity;
        }

        private StorageTableOperation(TableOperation sdk, string retrievePartitionKey, string retrieveRowKey,
            IEntityResolver retrieveEntityResolver)
        {
            _sdk = sdk;
            _operationType = TableOperationType.Retrieve;
            _retrievePartitionKey = retrievePartitionKey;
            _retrieveRowKey = retrieveRowKey;
            _retrieveEntityResolver = retrieveEntityResolver;
        }

        /// <inheritdoc />
        public TableOperationType OperationType
        {
            get { return _operationType; }
        }

        /// <inheritdoc />
        public ITableEntity Entity
        {
            get { return _entity; }
        }

        /// <inheritdoc />
        public string RetrievePartitionKey
        {
            get { return _retrievePartitionKey; }
        }

        /// <inheritdoc />
        public string RetrieveRowKey
        {
            get { return _retrieveRowKey; }
        }

        /// <inheritdoc />
        public IEntityResolver RetrieveEntityResolver
        {
            get { return _retrieveEntityResolver; }
        }

        /// <summary>Gets the underlying <see cref="TableOperation"/>.</summary>
        public TableOperation SdkObject
        {
            get { return _sdk; }
        }

        /// <summary>Creates an operation to insert an entity.</summary>
        /// <param name="entity">The entity to insert.</param>
        /// <returns>An operation to insert an entity.</returns>
        public static StorageTableOperation Insert(ITableEntity entity)
        {
            TableOperation sdkOperation = TableOperation.Insert(entity);
            return new StorageTableOperation(sdkOperation, TableOperationType.Insert, entity);
        }

        /// <summary>Creates an operation to insert or replace an entity.</summary>
        /// <param name="entity">The entity to insert or replace.</param>
        /// <returns>An operation to insert or replace an entity.</returns>
        public static StorageTableOperation InsertOrReplace(ITableEntity entity)
        {
            TableOperation sdkOperation = TableOperation.InsertOrReplace(entity);
            return new StorageTableOperation(sdkOperation, TableOperationType.InsertOrReplace, entity);
        }

        /// <summary>Creates an operation to replace an entity.</summary>
        /// <param name="entity">The entity to replace.</param>
        /// <returns>An operation to replace an entity.</returns>
        public static StorageTableOperation Replace(ITableEntity entity)
        {
            TableOperation sdkOperation = TableOperation.Replace(entity);
            return new StorageTableOperation(sdkOperation, TableOperationType.Replace, entity);
        }

        /// <summary>Creates an operation to retrieve an entity.</summary>
        /// <typeparam name="TElement">The type of entity to retrieve.</typeparam>
        /// <param name="partitionKey">The partition key of the entity to retrieve.</param>
        /// <param name="rowKey">The row key of the entity to retrieve.</param>
        /// <returns>An operation to retrieve an entity.</returns>
        public static StorageTableOperation Retrieve<TElement>(string partitionKey, string rowKey)
            where TElement : ITableEntity, new()
        {
            TableOperation sdkOperation = TableOperation.Retrieve<TElement>(partitionKey, rowKey);
            IEntityResolver resolver = new TypeEntityResolver<TElement>();
            return new StorageTableOperation(sdkOperation, partitionKey, rowKey, resolver);
        }
    }
}
