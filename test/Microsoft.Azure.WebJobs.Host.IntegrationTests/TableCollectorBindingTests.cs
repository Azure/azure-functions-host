// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.IntegrationTests
{
    public class TableCollectorBindingTests
    {
        private const string TableName = "inttable";

        private const string TableNameCollector = "TableC1";
        private const string TableNameCollectorDynamic = "TableC1b";
        private const string TableNameAsyncCollector = "TableC2";
        private const string TableNameCollectorPOCO = "TableC3";
        private const string TableNameAsyncCollectorPOCO = "TableC4";

        [Fact]
        public void TestCollector()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(TableName);

            var lc = JobHostFactory.Create<Program>();

            try
            {
                lc.Call("Collector");

                TableQuery query = new TableQuery()
                    .Where(TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableNameCollector),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableNameCollector)))
                    .Take(1);
                DynamicTableEntity result = table.ExecuteQuery(query).FirstOrDefault();

                // Ensure expected row found
                Assert.NotNull(result);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        [Fact]
        public void TestCollectorDynamic()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(TableName);

            var lc = JobHostFactory.Create<Program>();

            try
            {
                lc.Call("CollectorDynamic");

                TableQuery query = new TableQuery()
                    .Where(TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableNameCollectorDynamic),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableNameCollectorDynamic)))
                    .Take(1);
                DynamicTableEntity result = table.ExecuteQuery(query).FirstOrDefault();

                // Ensure expected row found
                Assert.NotNull(result);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        [Fact]
        public void TestAsyncCollector()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(TableName);

            try
            {
                var lc = JobHostFactory.Create<Program>();

                lc.Call("AsyncCollector");

                TableQuery query = new TableQuery()
                    .Where(TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableNameAsyncCollector),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableNameAsyncCollector)))
                    .Take(1);
                DynamicTableEntity result = table.ExecuteQuery(query).FirstOrDefault();

                // Ensure expected row found
                Assert.NotNull(result);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        [Fact]
        public void TestCollectorPOCO()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(TableName);

            var lc = JobHostFactory.Create<Program>();

            try
            {
                lc.Call("CollectorPOCO");

                TableQuery query = new TableQuery()
                    .Where(TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableNameCollectorPOCO),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableNameCollectorPOCO)))
                    .Take(1);
                DynamicTableEntity result = table.ExecuteQuery(query).FirstOrDefault();

                // Ensure expected row found
                Assert.NotNull(result);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        [Fact]
        public void TestAsyncCollectorPOCO()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(TableName);

            var lc = JobHostFactory.Create<Program>();

            try
            {
                lc.Call("AsyncCollectorPOCO");

                TableQuery query = new TableQuery()
                    .Where(TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, TableNameAsyncCollectorPOCO),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, TableNameAsyncCollectorPOCO)))
                    .Take(1);
                DynamicTableEntity result = table.ExecuteQuery(query).FirstOrDefault();

                // Ensure expected row found
                Assert.NotNull(result);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        [Fact]
        public void TestCollectorWrongETagThrows()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(TableName);

            var lc = JobHostFactory.Create<Program>();

            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => lc.Call("CollectorWrongETagThrows"));
                StorageException innerException = exception.InnerException as StorageException;
                Assert.NotNull(innerException);
                Assert.NotNull(innerException.RequestInformation);
                Assert.Equal(412, innerException.RequestInformation.HttpStatusCode);
                Assert.NotNull(innerException.RequestInformation.ExtendedErrorInformation);
                Assert.Equal("ConditionNotMet", innerException.RequestInformation.ExtendedErrorInformation.ErrorCode);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        class Program
        {
            /// <summary>
            /// Covers:
            /// - collector ITableEntity writing with ITableEntity
            /// </summary>
            public static void Collector(
                [Table(TableName)] ICollector<ITableEntity> table)
            {
                const string tableKeys = TableNameCollector;

                ITableEntity result = new DynamicTableEntity
                {
                    PartitionKey = tableKeys,
                    RowKey = tableKeys,
                    Properties = new Dictionary<string, EntityProperty>
                    {
                        { "Text", new EntityProperty("icollector-tableentity") },
                        { "Number", new EntityProperty("1") }
                    }
                };

                table.Add(result);
            }

            /// <summary>
            /// Covers:
            /// - collector ITableEntity writing with DynamicTableEntity
            /// </summary>
            public static void CollectorDynamic(
                [Table(TableName)] ICollector<DynamicTableEntity> table)
            {
                const string tableKeys = TableNameCollectorDynamic;

                DynamicTableEntity result = new DynamicTableEntity
                {
                    PartitionKey = tableKeys,
                    RowKey = tableKeys,
                    Properties = new Dictionary<string, EntityProperty>
                    {
                        { "Text", new EntityProperty("icollector-tableentity") },
                        { "Number", new EntityProperty("1") }
                    }
                };

                table.Add(result);
            }

            /// <summary>
            /// Covers:
            /// - iasynccollector tableentity writing
            /// </summary>
            public static void AsyncCollector(
                [Table(TableName)] IAsyncCollector<ITableEntity> table)
            {
                const string tableKeys = TableNameAsyncCollector;

                DynamicTableEntity result = new DynamicTableEntity
                {
                    PartitionKey = tableKeys,
                    RowKey = tableKeys,
                    Properties = new Dictionary<string, EntityProperty>
                {
                    { "Text", new EntityProperty("iasyncollector-tableentity") },
                    { "Number", new EntityProperty("2") }
                }
                };

                table.AddAsync(result);
            }

            /// <summary>
            /// Covers:
            /// - collector POCO writing
            /// </summary>
            public static void CollectorPOCO(
                [Table(TableName)] ICollector<PocoEntity> table)
            {
                const string tableKeys = TableNameCollectorPOCO;

                PocoEntity result = new PocoEntity
                {
                    PartitionKey = tableKeys,
                    RowKey = tableKeys,
                    Text = "ICollectorPoco",
                    Number = 3
                };

                table.Add(result);
            }

            /// <summary>
            /// Covers:
            /// - async collector POCO writing
            /// </summary>
            public static void AsyncCollectorPOCO(
                [Table(TableName)] IAsyncCollector<PocoEntity> table)
            {
                const string tableKeys = TableNameAsyncCollectorPOCO;

                PocoEntity result = new PocoEntity
                {
                    PartitionKey = tableKeys,
                    RowKey = tableKeys,
                    Text = "IAsyncCollector-Poco",
                    Number = 4
                };

                table.AddAsync(result);
            }

            /// <summary>
            /// Covers:
            /// - etag throws
            /// </summary>
            public static void CollectorWrongETagThrows(
                [Table(TableName)] ICollector<ITableEntity> collector,
                [Table(TableName)] CloudTable table)
            {
                const string tableKeys = "testETag";

                // Create the initial version of the entity
                table.Execute(TableOperation.Insert(new DynamicTableEntity
                {
                    PartitionKey = tableKeys,
                    RowKey = tableKeys
                }));

                // Get the initial ETag.
                string eTag = table.Execute(TableOperation.Retrieve(tableKeys, tableKeys)).Etag;

                // Update the entity to invalidate the initial ETag.
                table.Execute(TableOperation.Replace(new DynamicTableEntity
                {
                    PartitionKey = tableKeys,
                    RowKey = tableKeys,
                    ETag = "*",
                    Properties = new Dictionary<string, EntityProperty>
                    {
                        { "Text", new EntityProperty("valid")}
                    }
                }));

                // Add the item with the old ETag to the collector
                DynamicTableEntity result = new DynamicTableEntity
                {
                    PartitionKey = tableKeys,
                    RowKey = tableKeys,
                    ETag = eTag,
                    Properties = new Dictionary<string, EntityProperty>()
                    {
                        { "Text", new EntityProperty("invalid") }
                    }
                };

                collector.Add(result);
            }
        }

        public class PocoEntity
        {
            public string PartitionKey { get; set; }
            public string RowKey { get; set; }
            public string Text { get; set; }
            public int Number { get; set; }
        }

        public class CustomObject
        {
            public string Text { get; set; }

            public int Number { get; set; }
        }
    }
}
