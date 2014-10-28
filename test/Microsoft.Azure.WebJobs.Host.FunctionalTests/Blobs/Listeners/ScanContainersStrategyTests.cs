// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Blobs.Listeners;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.Blobs.Listeners
{
    public class ScanContainersStrategyTests
    {
        [Fact]
        public void TestBlobListener()
        {
            const string containerName = "container";
            IStorageAccount account = CreateFakeStorageAccount();
            IStorageBlobContainer container = account.CreateBlobClient().GetContainerReference(containerName);
            IBlobNotificationStrategy product = new ScanContainersStrategy();
            LambdaBlobTriggerExecutor executor = new LambdaBlobTriggerExecutor();
            product.Register(container, executor);

            executor.ExecuteLambda = (_) =>
            {
                throw new InvalidOperationException("shouldn't be any blobs in the container");
            };
            product.Execute();

            const string expectedBlobName = "foo1.csv";
            IStorageBlockBlob blob = container.GetBlockBlobReference(expectedBlobName);
            container.CreateIfNotExists();
            blob.UploadText("ignore");

            int count = 0;
            executor.ExecuteLambda = (b) =>
            {
                count++;
                Assert.Equal(expectedBlobName, b.Name);
                return true;
            };
            product.Execute();
            Assert.Equal(1, count);

            // Now run again; shouldn't show up. 
            executor.ExecuteLambda = (_) =>
            {
                throw new InvalidOperationException("shouldn't retrigger the same blob");
            };
            product.Execute();
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }

        private class LambdaBlobTriggerExecutor : ITriggerExecutor<IStorageBlob>
        {
            public Func<IStorageBlob, bool> ExecuteLambda { get; set; }

            public Task<bool> ExecuteAsync(IStorageBlob value, CancellationToken cancellationToken)
            {
                return Task.FromResult(ExecuteLambda.Invoke(value));
            }
        }
    }
}
