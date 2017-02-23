// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
