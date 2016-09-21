// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class BlobTriggerTests
    {
        private const string ContainerName = "container";
        private const string BlobName = "blob";
        private const string BlobPath = ContainerName + "/" + BlobName;
        private const string OutputBlobName = "blob.out";
        private const string OutputBlobPath = ContainerName + "/" + OutputBlobName;

        [Fact]
        public void BlobTrigger_IfBoundToCloudBlob_Binds()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobContainer container = CreateContainer(account, ContainerName);
            IStorageBlockBlob blob = container.GetBlockBlobReference(BlobName);
            CloudBlockBlob expectedBlob = blob.SdkObject;
            blob.UploadText("ignore");

            // Act
            ICloudBlob result = RunTrigger<ICloudBlob>(account, typeof(BindToCloudBlobProgram),
                (s) => BindToCloudBlobProgram.TaskSource = s);

            // Assert
            Assert.Equal(expectedBlob.Uri, result.Uri);
        }

        private class BindToCloudBlobProgram
        {
            public static TaskCompletionSource<ICloudBlob> TaskSource { get; set; }

            public static void Run([BlobTrigger(BlobPath)] ICloudBlob blob)
            {
                TaskSource.TrySetResult(blob);
            }
        }

        private class PoisonBlobProgram
        {
            public static TaskCompletionSource<string> TaskSource { get; set; }

            public static void PutInPoisonQueue([BlobTrigger(BlobPath)] string message)
            {
                throw new InvalidOperationException();
            }

            public static void ReceiveFromPoisonQueue([QueueTrigger("webjobs-blobtrigger-poison")] string message)
            {
                TaskSource.TrySetResult(message);
            }
        }

        [Fact]
        public void BlobTrigger_IfWritesToSecondBlobTrigger_TriggersOutputQuickly()
        {
            // Arrange
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobContainer container = CreateContainer(account, ContainerName);
            IStorageBlockBlob inputBlob = container.GetBlockBlobReference(BlobName);
            inputBlob.UploadText("abc");

            // Act
            RunTrigger<object>(account, typeof(BlobTriggerToBlobTriggerProgram),
                (s) => BlobTriggerToBlobTriggerProgram.TaskSource = s);

            // Assert
            IStorageBlockBlob outputBlob = container.GetBlockBlobReference(OutputBlobName);
            string content = outputBlob.DownloadText();
            Assert.Equal("*abc*", content);
        }

        private class BlobTriggerToBlobTriggerProgram
        {
            private const string CommittedQueueName = "committed";
            private const string IntermediateBlobPath = ContainerName + "/" + "blob.middle";

            public static TaskCompletionSource<object> TaskSource { get; set; }

            public static void StepOne([BlobTrigger(BlobPath)] TextReader input,
                [Blob(IntermediateBlobPath)] TextWriter output)
            {
                string content = input.ReadToEnd();
                output.Write(content);
            }

            public static void StepTwo([BlobTrigger(IntermediateBlobPath)] TextReader input,
                [Blob(OutputBlobPath)] TextWriter output, [Queue(CommittedQueueName)] out string committed)
            {
                string content = input.ReadToEnd();
                output.Write("*" + content + "*");
                committed = String.Empty;
            }

            public static void StepThree([QueueTrigger(CommittedQueueName)] string ignore)
            {
                TaskSource.TrySetResult(null);
            }
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

        private static TResult RunTrigger<TResult>(IStorageAccount account, Type programType,
            Action<TaskCompletionSource<TResult>> setTaskSource, IEnumerable<string> ignoreFailureFunctions)
        {
            return FunctionalTest.RunTrigger<TResult>(account, programType, setTaskSource, ignoreFailureFunctions);
        }
    }
}
