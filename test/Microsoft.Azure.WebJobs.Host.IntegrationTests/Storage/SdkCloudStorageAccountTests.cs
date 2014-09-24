// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.IntegrationTests.Storage
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
                IStorageAccount product = CreateProductUnderTest(sdkAccount);
                IStorageQueueClient client = product.CreateQueueClient();
                Assert.NotNull(client); // Guard
                IStorageQueue queue = client.GetQueueReference(queueName);
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
        public void QueueAddMessage_AddsMessage()
        {
            // Arrange
            CloudStorageAccount sdkAccount = CreateSdkAccount();
            string queueName = GetQueueName("add-message");

            CloudQueue sdkQueue = CreateSdkQueue(sdkAccount, queueName);
            sdkQueue.CreateIfNotExists();

            try
            {
                string expectedContent = "hello";

                IStorageAccount product = CreateProductUnderTest(sdkAccount);
                IStorageQueueClient client = product.CreateQueueClient();
                Assert.NotNull(client); // Guard
                IStorageQueue queue = client.GetQueueReference(queueName);
                Assert.NotNull(queue); // Guard

                CloudQueueMessage message = new CloudQueueMessage(expectedContent);
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

        private static IStorageAccount CreateProductUnderTest(CloudStorageAccount sdkAccount)
        {
            return new StorageAccount(sdkAccount);
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
            string name = ConnectionStringNames.Dashboard;
            string value = new AmbientConnectionStringProvider().GetConnectionString(name);

            if (String.IsNullOrEmpty(value))
            {
                string message = String.Format(
                    "This test needs an Azure storage connection string to run. Please set the '{0}' environment " +
                    "variable or App.config connection string before running this test.", AmbientConnectionStringProvider.Prefix + name);
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

        private class SimpleEntity : TableEntity
        {
            public string Value { get; set; }
        }
    }
}
