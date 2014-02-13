using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.WindowsAzure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;
using Microsoft.WindowsAzure.StorageClient;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.IntegrationTests.Storage.Queues
{
    public class SdkCloudStorageAccountTests
    {
        [Fact]
        public void CloudQueueCreate_IfNotExist_CreatesQueue()
        {
            // Arrange
            CloudStorageAccount sdkAccount = CreateSdkAccount();
            string queueName = GetQueueName("create-queue");

            CloudQueue sdkQueue = CreateSdkQueue(sdkAccount, queueName);

            try
            {
                ICloudStorageAccount product = CreateProductUnderTest(sdkAccount);
                ICloudQueueClient client = product.CreateCloudQueueClient();
                Assert.NotNull(client); // Guard
                ICloudQueue queue = client.GetQueueReference(queueName);
                Assert.NotNull(queue); // Guard

                // Act
                queue.CreateIfNotExists();

                // Assert
                Assert.True(sdkQueue.Exists());
            }
            finally
            {
                if (sdkQueue.Exists())
                {
                    sdkQueue.Delete();
                }
            }
        }

        [Fact]
        public void CloudQueueAddMessage_AddsMessage()
        {
            // Arrange
            CloudStorageAccount sdkAccount = CreateSdkAccount();
            string queueName = GetQueueName("add-message");

            CloudQueue sdkQueue = CreateSdkQueue(sdkAccount, queueName);
            sdkQueue.CreateIfNotExist();

            try
            {
                string expectedContent = "hello";

                ICloudStorageAccount product = CreateProductUnderTest(sdkAccount);
                ICloudQueueClient client = product.CreateCloudQueueClient();
                Assert.NotNull(client); // Guard
                ICloudQueue queue = client.GetQueueReference(queueName);
                Assert.NotNull(queue); // Guard

                ICloudQueueMessage message = queue.CreateMessage(expectedContent);
                Assert.NotNull(message); // Guard

                // Act
                queue.AddMessage(message);

                // Assert
                CloudQueueMessage sdkMessage = sdkQueue.GetMessage();
                Assert.NotNull(sdkMessage);
                Assert.Equal(expectedContent, sdkMessage.AsString);
            }
            finally
            {
                sdkQueue.Delete();
            }
        }

        [Fact]
        public void CloudTableGetOrInsert_IfTableDoesNotExist_CreatesTable()
        {
            // Arrange
            CloudStorageAccount sdkAccount = CreateSdkAccount();
            string tableName = GetTableName("CreateTable");

            CloudTableClient sdkClient = sdkAccount.CreateCloudTableClient();

            try
            {
                ICloudStorageAccount product = CreateProductUnderTest(sdkAccount);
                ICloudTableClient client = product.CreateCloudTableClient();
                Assert.NotNull(client); // Guard
                ICloudTable table = client.GetTableReference(tableName);
                Assert.NotNull(table); // Guard

                SimpleEntity entity = new SimpleEntity();

                // Act
                table.GetOrInsert(entity);

                // Assert
                Assert.True(sdkClient.DoesTableExist(tableName));
            }
            finally
            {
                if (sdkClient.DoesTableExist(tableName))
                {
                    sdkClient.DeleteTable(tableName);
                }
            }
        }

        [Fact]
        public void CloudTableGetOrInsert_IfEntityDoesNotExist_AddsEntity()
        {
            // Arrange
            CloudStorageAccount sdkAccount = CreateSdkAccount();
            string tableName = GetTableName("AddEntityDoesNotExist");

            CloudTableClient sdkClient = sdkAccount.CreateCloudTableClient();
            TableServiceContext sdkContext = sdkClient.GetDataServiceContext();
            sdkClient.CreateTableIfNotExist(tableName);

            try
            {
                ICloudStorageAccount product = CreateProductUnderTest(sdkAccount);
                ICloudTableClient client = product.CreateCloudTableClient();
                Assert.NotNull(client); // Guard
                ICloudTable table = client.GetTableReference(tableName);
                Assert.NotNull(table); // Guard
                SimpleEntity insertEntity = new SimpleEntity
                {
                    PartitionKey = "PK",
                    RowKey = "RK",
                    Value = "Foo"
                };

                // Act
                SimpleEntity result = table.GetOrInsert(insertEntity);

                // Assert
                Assert.Same(insertEntity, result);
            }
            finally
            {
                sdkClient.DeleteTable(tableName);
            }
        }

        [Fact]
        public void CloudTableGetOrInsert_IfEntityExists_ReturnsExistingEntity()
        {
            // Arrange
            CloudStorageAccount sdkAccount = CreateSdkAccount();
            string tableName = GetTableName("AddEntityExists");

            CloudTableClient sdkClient = sdkAccount.CreateCloudTableClient();
            TableServiceContext sdkContext = sdkClient.GetDataServiceContext();
            sdkClient.CreateTableIfNotExist(tableName);
            try
            {
                SimpleEntity existingEntity = new SimpleEntity
                {
                    PartitionKey = "PK",
                    RowKey = "RK",
                    Value = "ExistingValue"
                };
                sdkContext.AddObject(tableName, existingEntity);
                sdkContext.SaveChanges();

                ICloudStorageAccount product = CreateProductUnderTest(sdkAccount);
                ICloudTableClient client = product.CreateCloudTableClient();
                Assert.NotNull(client); // Guard
                ICloudTable table = client.GetTableReference(tableName);
                Assert.NotNull(table); // Guard
                SimpleEntity newEntity = new SimpleEntity
                {
                    PartitionKey = "PK",
                    RowKey = "RK",
                    Value = "NewValue"
                };

                // Act
                SimpleEntity result = table.GetOrInsert(newEntity);

                // Assert
                Assert.NotNull(result);
                Assert.Equal(existingEntity.PartitionKey, result.PartitionKey);
                Assert.Equal(existingEntity.RowKey, result.RowKey);
                Assert.Equal(existingEntity.Value, result.Value);
            }
            finally
            {
                sdkClient.DeleteTable(tableName);
            }
        }

        //[Fact]
        //public void CloudTableGetEntity_ReturnsEntity()
        //{
        //    // Arrange
        //    CloudStorageAccount sdkAccount = CreateSdkAccount();
        //    string tableName = GetTableName("GetEntity");

        //    CloudTableClient sdkClient = sdkAccount.CreateCloudTableClient();
        //    TableServiceContext sdkContext = sdkClient.GetDataServiceContext();
        //    sdkClient.CreateTableIfNotExist(tableName);
        //    try
        //    {
        //        SimpleEntity existingEntity = new SimpleEntity
        //        {
        //            PartitionKey = "PK",
        //            RowKey = "RK",
        //            Value = "Foo"
        //        };
        //        sdkContext.AddObject(tableName, existingEntity);
        //        sdkContext.SaveChanges();

        //        ICloudStorageAccount product = CreateProductUnderTest(sdkAccount);
        //        ICloudTableClient client = product.CreateCloudTableClient();
        //        Assert.NotNull(client); // Guard
        //        ICloudTable table = client.GetTableReference(tableName);
        //        Assert.NotNull(table); // Guard

        //        // Act
        //        SimpleEntity foundEntity = table.GetEntity<SimpleEntity>("PK", "RK");

        //        // Assert
        //        Assert.NotNull(foundEntity);
        //        Assert.Equal("Foo", foundEntity.Value);
        //    }
        //    finally
        //    {
        //        sdkClient.DeleteTable(tableName);
        //    }
        //}

        [Fact]
        public void CloudTableGetOrInsert_IfEntityIsNull_Throws()
        {
            // Arrange
            CloudStorageAccount sdkAccount = CreateSdkAccount();
            ICloudStorageAccount product = CreateProductUnderTest(sdkAccount);
            ICloudTableClient client = product.CreateCloudTableClient();
            Assert.NotNull(client); // Guard
            ICloudTable table = client.GetTableReference("IgnoreTable");
            Assert.NotNull(table); // Guard

            SimpleEntity entity = null;

            // Act & Assert
            ExceptionAssert.ThrowsArgumentNull(() => table.GetOrInsert(entity), "entity");
        }

        private static ICloudStorageAccount CreateProductUnderTest(CloudStorageAccount account)
        {
            return new SdkCloudStorageAccount(account);
        }

        private static CloudStorageAccount CreateSdkAccount()
        {
            return CloudStorageAccount.Parse(GetConnectionString());
        }

        private static CloudQueue CreateSdkQueue(CloudStorageAccount sdkAccount, string queueName)
        {
            CloudQueueClient sdkClient = sdkAccount.CreateCloudQueueClient();
            return sdkClient.GetQueueReference(queueName);
        }

        private static string GetConnectionString()
        {
            string name = "AzureJobsRuntime";

            string value = new DefaultConnectionStringProvider().GetConnectionString(name);

            if (String.IsNullOrEmpty(value))
            {
                string message = String.Format(
                    "This test needs an Azure storage connection string to run. Please set the '{0}' environment " +
                    "variable or App.config connection string before running this test.", name);
                throw new InvalidOperationException(message);
            }

            return value;
        }

        private static string GetQueueName(string infix)
        {
            return String.Format("test-{0}-{1:N}", infix, Guid.NewGuid());
        }

        private static string GetTableName(string infix)
        {
            return String.Format("Test{0}{1:N}", infix, Guid.NewGuid());
        }

        private class SimpleEntity : TableServiceEntity
        {
            public string Value { get; set; }
        }
    }
}
