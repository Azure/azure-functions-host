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
    // Test model binding with Azure 2.0 sdk 
    public class Sdk2IntegrationTests
    {
        // Test binding a parameter to the CloudStorageAccount that a function is uploaded to. 
        [Fact]
        public void TestBindCloudStorageAccount()
        {
            var lc = JobHostFactory.Create<Program>();
            lc.Call("FuncCloudStorageAccount");
        }

        [Fact]
        public void TestIBlob()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference("daas-test");
            container.CreateIfNotExists();

            var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            var block = container.GetBlockBlobReference("block");
            block.UploadFromStream(stream);

            var page = container.GetPageBlobReference("page");
            page.UploadFromStream(stream);

            var lc = JobHostFactory.Create<Program>();

            lc.Call("IBlob", new { page = page });

            lc.Call("BlockBlob", new { block = block });

            lc.Call("PageBlob", new { page = page });

            container.DeleteIfExists();
        }

        [Fact]
        public void TestMissingIBlob()
        {
            var lc = JobHostFactory.Create<Program>();
            ExceptionAssert.ThrowsInvalidOperation(() => lc.Call("IBlobMissing"),
                "Missing value for trigger parameter 'missing'.");
            ExceptionAssert.ThrowsInvalidOperation(() => lc.Call("BlockBlobMissing"),
                "Missing value for trigger parameter 'block'.");
            ExceptionAssert.ThrowsInvalidOperation(() => lc.Call("PageBlobMissing"),
                "Missing value for trigger parameter 'page'.");
        }

        [Fact]
        public void TestQueue()
        {
            var lc = JobHostFactory.Create<Program>();
            lc.Call("Queue");
            Assert.True(Program._QueueInvoked);
        }

        [Fact]
        public void TestQueueBadName()
        {
            var host = JobHostFactory.Create<ProgramBadQueueName>(null);

            // indexer should notice bad queue name and fail immediately
            Assert.Throws<FunctionIndexingException>(() => host.RunAndBlock());
        }

        public void TestTable()
        {
            var lc = JobHostFactory.Create<Program>();
            lc.Call("Table");
            Assert.True(Program.TableInvoked);
        }

        [Fact]
        public void TestIQueryable()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference("QueryableTest");
            table.CreateIfNotExists();

            try
            {
                ITableEntity[] entities = new ITableEntity[]
                {
                    CreateEntity(1, "A"),
                    CreateEntity(2, "B"),
                    CreateEntity(3, "B"),
                    CreateEntity(4, "C")
                };

                TableBatchOperation batch = new TableBatchOperation();

                foreach (ITableEntity entity in entities)
                {
                    batch.Add(TableOperation.Insert(entity));
                }

                table.ExecuteBatch(batch);

                var host = JobHostFactory.Create<Program>();
                host.Call("CountEntitiesWithStringPropertyB");

                Assert.Equal(2, Program.EntitiesWithStringPropertyB);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        [Fact]
        public void TestITableEntity()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference("ITableEntityTest");
            table.CreateIfNotExists();

            try
            {
                DynamicTableEntity entity = new DynamicTableEntity("PK", "RK");
                entity.Properties["Value"] = new EntityProperty("Foo");
                table.Execute(TableOperation.Insert(entity));

                var host = JobHostFactory.Create<Program>();
                host.Call("TestITableEntity");

                DynamicTableEntity updatedEntity =
                    (DynamicTableEntity)table.Execute(TableOperation.Retrieve("PK", "RK")).Result;

                Assert.Equal(1, updatedEntity.Properties.Count);
                Assert.Equal(new EntityProperty("Bar"), updatedEntity.Properties["Value"]);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        [Fact]
        public void TestPocoTableEntity()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference("PocoTableEntityTest");
            table.CreateIfNotExists();

            try
            {
                DynamicTableEntity entity = new DynamicTableEntity("PK", "RK");
                entity.Properties["Fruit"] = new EntityProperty("Banana");
                entity.Properties["Duration"] = new EntityProperty("\"00:00:01\"");
                entity.Properties["Value"] = new EntityProperty("Foo");
                table.Execute(TableOperation.Insert(entity));

                var host = JobHostFactory.Create<Program>();
                host.Call("TestPocoTableEntity");

                DynamicTableEntity updatedEntity =
                    (DynamicTableEntity)table.Execute(TableOperation.Retrieve("PK", "RK")).Result;

                Assert.Equal(3, updatedEntity.Properties.Count);
                Assert.Equal(new EntityProperty("Pear"), updatedEntity.Properties["Fruit"]);
                Assert.Equal(new EntityProperty("\"00:02:00\""), updatedEntity.Properties["Duration"]);
                Assert.Equal(new EntityProperty("Bar"), updatedEntity.Properties["Value"]);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        [Fact]
        public void TestITableEntityConcurrency()
        {
            TestTableEntityConcurrency(
                tableName: "ITableEntityConcurrencyTest",
                methodName: "TestITableEntityConcurrency");
        }

        [Fact]
        public void TestPocoTableEntityConcurrency()
        {
            TestTableEntityConcurrency(
                tableName: "PocoTableEntityConcurrencyTest",
                methodName: "TestPocoTableEntityConcurrency");
        }

        private static void TestTableEntityConcurrency(string tableName, string methodName)
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(tableName);
            table.CreateIfNotExists();

            try
            {
                DynamicTableEntity entity = new DynamicTableEntity("PK", "RK");
                entity.Properties["Value"] = new EntityProperty("Foo");
                table.Execute(TableOperation.Insert(entity));

                var host = JobHostFactory.Create<Program>();

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => host.Call(methodName));
                Assert.Equal("Error while handling parameter entity after function returned:", exception.Message);

                DynamicTableEntity updatedEntity =
                    (DynamicTableEntity)table.Execute(TableOperation.Retrieve("PK", "RK")).Result;

                Assert.Equal(1, updatedEntity.Properties.Count);
                Assert.Equal(new EntityProperty("FooBackground"), updatedEntity.Properties["Value"]);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        private static DynamicTableEntity CreateEntity(int row, string property)
        {
            return new DynamicTableEntity("PK", "RK" + row.ToString(), null, CreateProperties("StringProperty", property));
        }

        private static IDictionary<string, EntityProperty> CreateProperties(string key, string value)
        {
            return new Dictionary<string, EntityProperty>
            {
                { key, EntityProperty.GeneratePropertyForString(value) }
            };
        }

        class ProgramBadQueueName
        {
            public static void QueueBadName([Queue("IllegalName")] CloudQueue queue)
            {
                throw new NotSupportedException("shouldnt get invoked");
            }
        }

        class Program
        {
            // Test binding to CloudStorageAccount 
            [NoAutomaticTrigger]
            public static void FuncCloudStorageAccount(CloudStorageAccount account)
            {
                var account2 = CloudStorageAccount.DevelopmentStorageAccount;

                Assert.Equal(account.ToString(), account2.ToString());
            }

            public static bool _QueueInvoked;

            public static bool TableInvoked { get; set; }

            public static int EntitiesWithStringPropertyB { get; set; }

            public static int RowCount { get; set; }

            public static void Queue([Queue("mytestqueue")]CloudQueue queue)
            {
                _QueueInvoked = true;
                Assert.NotNull(queue);
            }

            public static void IBlobMissing(
                [BlobTrigger("daas-test/missing")] ICloudBlob missing)
            {
                Assert.Null(missing);
            }

            public static void IBlob(
                [BlobTrigger("daas-test/page")] ICloudBlob page,
                [Blob("daas-test/block")] ICloudBlob block)
            {
                Assert.NotNull(page);
                Assert.NotNull(block);

                Assert.Equal(BlobType.PageBlob, page.BlobType);
                Assert.Equal(BlobType.BlockBlob, block.BlobType);
            }

            public static void BlockBlob(
               [BlobTrigger("daas-test/block")] CloudBlockBlob block)
            {
                Assert.Equal(BlobType.BlockBlob, block.BlobType);
            }

            public static void PageBlob(
                [BlobTrigger("daas-test/page")] CloudPageBlob page)
            {
                Assert.Equal(BlobType.PageBlob, page.BlobType);
            }


            public static void BlockBlobMissing(
               [BlobTrigger("daas-test/missing")] CloudBlockBlob block)
            {
                Assert.Null(block);
            }

            public static void PageBlobMissing(
                [BlobTrigger("daas-test/page")] CloudPageBlob page)
            {
                Assert.Null(page);
            }

            public static void Table([Table("DaasTestTable")] CloudTable table)
            {
                Assert.NotNull(table);
                TableInvoked = true;
            }

            public class QueryableTestEntity : TableEntity
            {
                public string StringProperty { get; set; }
            }

            public static void CountEntitiesWithStringPropertyB([Table("QueryableTest")] IQueryable<QueryableTestEntity> table)
            {
                IQueryable<QueryableTestEntity> query = from QueryableTestEntity entity in table
                                                        where entity.StringProperty == "B"
                                                        select entity;
                EntitiesWithStringPropertyB = query.ToArray().Count();
            }

            public class ValueTableEntity : TableEntity
            {
                public string Value { get; set; }
            }

            public static void TestITableEntity([Table("ITableEntityTest", "PK", "RK")] ValueTableEntity entity)
            {
                Assert.NotNull(entity);
                Assert.Equal("Foo", entity.Value);

                entity.Value = "Bar";
            }

            public static void TestITableEntityConcurrency(
                [Table("ITableEntityConcurrencyTest", "PK", "RK")] ValueTableEntity entity,
                [Table("ITableEntityConcurrencyTest")] CloudTable table)
            {
                Assert.NotNull(entity);
                Assert.Equal("Foo", entity.Value);

                // Update the entity to invalidate the version read by this method.
                table.Execute(TableOperation.Replace(new ValueTableEntity
                {
                    PartitionKey = "PK",
                    RowKey = "RK",
                    ETag = "*",
                    Value = "FooBackground"
                }));

                // The attempted update by this method should now fail.
                entity.Value = "Bar";
            }

            public class PocoTableEntity
            {
                public Fruit Fruit { get; set; }
                public TimeSpan Duration { get; set; }
                public string Value { get; set; }
            }

            public enum Fruit
            {
                Apple,
                Banana,
                Pear,
            }

            public static void TestPocoTableEntity([Table("PocoTableEntityTest", "PK", "RK")] PocoTableEntity entity)
            {
                Assert.NotNull(entity);
                Assert.Equal(Fruit.Banana, entity.Fruit);
                Assert.Equal(TimeSpan.FromSeconds(1), entity.Duration);
                Assert.Equal("Foo", entity.Value);

                entity.Fruit = Fruit.Pear;
                entity.Duration = TimeSpan.FromMinutes(2);
                entity.Value = "Bar";
            }

            public static void TestPocoTableEntityConcurrency(
                [Table("PocoTableEntityConcurrencyTest", "PK", "RK")] PocoTableEntity entity,
                [Table("PocoTableEntityConcurrencyTest")] CloudTable table)
            {
                Assert.NotNull(entity);

                // Update the entity to invalidate the version read by this method.
                table.Execute(TableOperation.Replace(new ValueTableEntity
                {
                    PartitionKey = "PK",
                    RowKey = "RK",
                    ETag = "*",
                    Value = "FooBackground"
                }));

                // The attempted update by this method should now fail.
                entity.Value = "Bar";
            }
        }
    }
}
