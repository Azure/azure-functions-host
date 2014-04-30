using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    // Test model binding with Azure 2.0 sdk 
    public class Sdk2IntegrationTests
    {
        // Test binding a parameter to the CloudStorageAccount that a function is uploaded to. 
        [Fact]
        public void TestBindCloudStorageAccount()
        {
            var lc = new TestJobHost<Program>();
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

            var lc = new TestJobHost<Program>();

            lc.Call("IBlob");

            lc.Call("BlockBlob");

            lc.Call("PageBlob");

            container.DeleteIfExists();
        }

        [Fact]
        public void TestMissingIBlob()
        {
            var lc = new TestJobHost<Program>();
            lc.Call("IBlobMissing");
            lc.Call("BlockBlobMissing");
            lc.Call("PageBlobMissing");
        }

        [Fact]
        public void TestQueue()
        {
            var lc = new TestJobHost<Program>();
            lc.Call("Queue");
            Assert.True(Program._QueueInvoked);
        }

        [Fact]
        public void TestQueueBadName()
        {
            // indexer should notice bad queue name and fail immediately
            Assert.Throws<IndexException>(() => new TestJobHost<ProgramBadQueueName>(null));
        }

        [Fact]
        public void TestTable()
        {
            var lc = new TestJobHost<Program>();
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

                var host = new TestJobHost<Program>();
                host.Call("CountEntitiesWithStringPropertyB");

                Assert.Equal(2, Program.EntitiesWithStringPropertyB);
            }
            finally
            {
                table.DeleteIfExists();
            }
        }

        [Fact]
        public void TestIQueryableMissingTable()
        {
            Program.RowCount = int.MinValue;

            var host = new TestJobHost<Program>();
            host.Call("IQueryableMissingTable");

            Assert.Equal(0, Program.RowCount);
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

                var host = new TestJobHost<Program>();
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
            [Description("test")]
            public static void QueueBadName(CloudQueue IllegalName)
            {
                throw new NotSupportedException("shouldnt get invoked");
            }
        }

        class Program
        {
            // Test binding to CloudStorageAccount 
            [Description("test")]
            public static void FuncCloudStorageAccount(CloudStorageAccount account)
            {
                var account2 = CloudStorageAccount.DevelopmentStorageAccount;

                Assert.Equal(account.ToString(), account2.ToString());
            }

            public static bool _QueueInvoked;

            public static bool TableInvoked { get; set; }

            public static int EntitiesWithStringPropertyB { get; set; }

            public static int RowCount { get; set; }

            [Description("test")]
            public static void Queue(CloudQueue mytestqueue)
            {
                _QueueInvoked = true;
                Assert.NotNull(mytestqueue);
            }

            public static void IBlobMissing(
                [BlobInput("daas-test/missing")] ICloudBlob missing)
            {
                Assert.Null(missing);
            }

            public static void IBlob(
                [BlobInput("daas-test/page")] ICloudBlob page,
                [BlobInput("daas-test/block")] ICloudBlob block)
            {
                Assert.NotNull(page);
                Assert.NotNull(block);

                Assert.Equal(BlobType.PageBlob, page.BlobType);
                Assert.Equal(BlobType.BlockBlob, block.BlobType);
            }

            public static void BlockBlob(
               [BlobInput("daas-test/block")] CloudBlockBlob block)
            {
                Assert.Equal(BlobType.BlockBlob, block.BlobType);
            }

            public static void PageBlob(
                [BlobInput("daas-test/page")] CloudPageBlob page)
            {
                Assert.Equal(BlobType.PageBlob, page.BlobType);
            }


            public static void BlockBlobMissing(
               [BlobInput("daas-test/missing")] CloudBlockBlob block)
            {
                Assert.Null(block);
            }

            public static void PageBlobMissing(
                [BlobInput("daas-test/page")] CloudPageBlob page)
            {
                Assert.Null(page);
            }

            [Description("test")]
            public static void Table([Table("DaasTestTable")] CloudTable table)
            {
                Assert.NotNull(table);
                TableInvoked = true;
            }

            public class QueryableTestEntity : TableEntity
            {
                public string StringProperty { get; set; }
            }

            [Description("test")]
            public static void CountEntitiesWithStringPropertyB([Table("QueryableTest")] IQueryable<QueryableTestEntity> table)
            {
                IQueryable<QueryableTestEntity> query = from QueryableTestEntity entity in table
                                                        where entity.StringProperty == "B"
                                                        select entity;
                EntitiesWithStringPropertyB = query.ToArray().Count();
            }

            [Description("test")]
            public static void IQueryableMissingTable([Table("NonExistingTable")] IQueryable<QueryableTestEntity> table)
            {
                int count = 0;
                
                foreach(QueryableTestEntity entity in table)
                {
                    ++count;
                }

                RowCount = count;
            }

            public class ValueTableEntity : TableEntity
            {
                public string Value { get; set; }
            }

            [Description("test")]
            public static void TestITableEntity([Table("ITableEntityTest", "PK", "RK")] ValueTableEntity entity)
            {
                Assert.NotNull(entity);
                Assert.Equal("Foo", entity.Value);

                entity.Value = "Bar";
            }
        }
    }
}
