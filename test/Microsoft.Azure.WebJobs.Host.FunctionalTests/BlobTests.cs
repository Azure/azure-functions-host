// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class BlobTests
    {
        private const string TriggerQueueName = "input";
        private const string ContainerName = "container";
        private const string BlobName = "blob";
        private const string BlobPath = ContainerName + "/" + BlobName;

        [Fact]
        public void Blob_IfBoundToCloudBlockBlob_BindsAndCreatesContainerButNotBlob()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage("ignore"));

            // Act
            CloudBlockBlob result = RunTrigger<CloudBlockBlob>(account, typeof(BindToCloudBlockBlobProgram),
                (s) => BindToCloudBlockBlobProgram.TaskSource = s);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(BlobName, result.Name);
            Assert.NotNull(result.Container);
            Assert.Equal(ContainerName, result.Container.Name);
            IStorageBlobContainer container = GetContainerReference(account, ContainerName);
            Assert.True(container.Exists());
            IStorageBlockBlob blob = container.GetBlockBlobReference(BlobName);
            Assert.False(blob.Exists());
        }

        [Fact]
        public void Blob_IfBoundToTextWriter_CreatesBlob()
        {
            // Arrange
            const string expectedContent = "message";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageQueue triggerQueue = CreateQueue(account, TriggerQueueName);
            triggerQueue.AddMessage(triggerQueue.CreateMessage(expectedContent));

            // Act
            RunTrigger(account, typeof(BindToTextWriterProgram));

            // Assert
            IStorageBlobContainer container = GetContainerReference(account, ContainerName);
            Assert.True(container.Exists());
            IStorageBlockBlob blob = container.GetBlockBlobReference(BlobName);
            Assert.True(blob.Exists());
            string content = blob.DownloadText();
            Assert.Equal(expectedContent, content);
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }

        private static IStorageQueue CreateQueue(IStorageAccount account, string queueName)
        {
            IStorageQueueClient client = account.CreateQueueClient();
            IStorageQueue queue = client.GetQueueReference(queueName);
            queue.CreateIfNotExists();
            return queue;
        }

        private static IStorageBlobContainer GetContainerReference(IStorageAccount account, string containerName)
        {
            IStorageBlobClient client = account.CreateBlobClient();
            return client.GetContainerReference(ContainerName);
        }

        private static void RunTrigger(IStorageAccount account, Type programType)
        {
            FunctionalTest.RunTrigger(account, programType);
        }

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource);
        }

        private class BindToCloudBlockBlobProgram
        {
            public static TaskCompletionSource<CloudBlockBlob> TaskSource { get; set; }

            public static void Run([QueueTrigger(TriggerQueueName)] CloudQueueMessage ignore,
                [Blob(BlobPath)] CloudBlockBlob blob)
            {
                TaskSource.TrySetResult(blob);
            }
        }

        private class BindToTextWriterProgram
        {
            public static void Run([QueueTrigger(TriggerQueueName)] string message,
                [Blob(BlobPath)] TextWriter blob)
            {
                blob.Write(message);
                blob.Flush();
            }
        }
    }
}
