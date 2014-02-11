using System;
using Microsoft.WindowsAzure.Jobs.Storage;
using Microsoft.WindowsAzure.Jobs.Storage.Queues;
using Microsoft.WindowsAzure.StorageClient;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.IntegrationTests.Storage.Queues
{
    public class SdkCloudStorageAccountTests
    {
        [Fact]
        public void CloudQueueCreateIfNotExist_CreatesQueue()
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

                try
                {
                    Assert.Equal(expectedContent, sdkMessage.AsString);
                }
                finally
                {
                    sdkQueue.DeleteMessage(sdkMessage);
                }

                Assert.True(sdkQueue.Exists());
            }
            finally
            {
                sdkQueue.Delete();
            }
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
    }
}
