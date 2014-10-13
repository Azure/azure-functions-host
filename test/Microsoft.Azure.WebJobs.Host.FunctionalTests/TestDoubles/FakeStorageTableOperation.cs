// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageTableOperation : IStorageTableOperation
    {
        private readonly TableOperationType _operationType;
        private readonly ITableEntity _entity;
        private readonly string _retrievePartitionKey;
        private readonly string _retrieveRowKey;
        private readonly IEntityResolver _retrieveEntityResolver;

        private FakeStorageTableOperation(TableOperationType operationType, ITableEntity entity)
        {
            _operationType = operationType;
            _entity = entity;
        }

        private FakeStorageTableOperation(string retrievePartitionKey, string retrieveRowKey,
            IEntityResolver retrieveEntityResolver)
        {
            _operationType = TableOperationType.Retrieve;
            _retrievePartitionKey = retrievePartitionKey;
            _retrieveRowKey = retrieveRowKey;
            _retrieveEntityResolver = retrieveEntityResolver;
        }

        public TableOperationType OperationType
        {
            get { return _operationType; }
        }

        public ITableEntity Entity
        {
            get { return _entity; }
        }

        public string RetrievePartitionKey
        {
            get { return _retrievePartitionKey; }
        }

        public string RetrieveRowKey
        {
            get { return _retrieveRowKey; }
        }

        public IEntityResolver RetrieveEntityResolver
        {
            get { return _retrieveEntityResolver; }
        }

        public static FakeStorageTableOperation Insert(ITableEntity entity)
        {
            return new FakeStorageTableOperation(TableOperationType.Insert, entity);
        }

        public static FakeStorageTableOperation Retrieve<TElement>(string partitionKey, string rowKey)
            where TElement : ITableEntity, new()
        {
            IEntityResolver resolver = new TypeEntityResolver<TElement>();
            return new FakeStorageTableOperation(partitionKey, rowKey, resolver);
        }

        public static FakeStorageTableOperation Replace(ITableEntity entity)
        {
            return new FakeStorageTableOperation(TableOperationType.Replace, entity);
        }
    }
}
