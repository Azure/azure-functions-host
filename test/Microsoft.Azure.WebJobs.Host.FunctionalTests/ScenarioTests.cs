// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class ScenarioTests
    {
        private const string ContainerName = "container";
        private const string BlobName = "blob";
        private const string BlobPath = ContainerName + "/" + BlobName;
        private const string OutputBlobName = "blob.out";
        private const string OutputBlobPath = ContainerName + "/" + OutputBlobName;
        private const string QueueName = "queue";

        [Fact]
        public void BlobTriggerToQueueTriggerToBlob_WritesFinalBlob()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobContainer container = CreateContainer(account, ContainerName);
            IStorageBlockBlob inputBlob = container.GetBlockBlobReference(BlobName);
            inputBlob.UploadText("15");

            // Act
            RunTrigger<object>(account, typeof(BlobTriggerToQueueTriggerToBlobProgram),
                (s) => BlobTriggerToQueueTriggerToBlobProgram.TaskSource = s);

            // Assert
            IStorageBlockBlob outputBlob = container.GetBlockBlobReference(OutputBlobName);
            string content = outputBlob.DownloadText();
            Assert.Equal("16", content);
        }

        private static IStorageBlobContainer CreateContainer(IStorageAccount account, string containerName)
        {
            IStorageBlobClient client = account.CreateBlobClient();
            IStorageBlobContainer container = client.GetContainerReference(containerName);
            container.CreateIfNotExists();
            return container;
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            var account = new FakeStorageAccount();

            // make sure our system containers are present
            var container = CreateContainer(account, "azure-webjobs-hosts");

            return account;
        }

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource);
        }

        private class BlobTriggerToQueueTriggerToBlobProgram
        {
            private const string CommittedQueueName = "committed";

            public static TaskCompletionSource<object> TaskSource { get; set; }

            public static void StepOne([BlobTrigger(BlobPath)] TextReader input, [Queue(QueueName)] out Payload output)
            {
                string content = input.ReadToEnd();
                int value = Int32.Parse(content);
                output = new Payload
                {
                    Value = value + 1,
                    Output = OutputBlobName
                };
            }

            public static void StepTwo([QueueTrigger(QueueName)] Payload input, int value,
                [Blob(ContainerName + "/{Output}")] TextWriter output, [Queue(CommittedQueueName)] out string committed)
            {
                Assert.Equal(input.Value, value);
                output.Write(value);
                committed = String.Empty;
            }

            public static void StepThree([QueueTrigger(CommittedQueueName)] string ignore)
            {
                TaskSource.TrySetResult(null);
            }
        }

        private class Payload
        {
            public int Value { get; set; }

            public string Output { get; set; }
        }
    }
}
