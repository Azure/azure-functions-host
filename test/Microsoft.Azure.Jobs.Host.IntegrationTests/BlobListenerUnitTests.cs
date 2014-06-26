using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Blobs.Listeners;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    public class BlobListenerTests
    {
        [Fact]
        public void TestBlobListener()
        {
            var account = CloudStorageAccount.DevelopmentStorageAccount;
            string containerName = @"daas-test-input";
            TestBlobClient.DeleteContainer(account, containerName);

            CloudBlobClient client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(containerName);
            IBlobNotificationStrategy strategy = new ScanContainersStrategy(CancellationToken.None);
            LambdaBlobTriggerExecutor executor = new LambdaBlobTriggerExecutor();
            strategy.Register(container, executor);

            executor.ExecuteLambda = (_) =>
            {
                throw new InvalidOperationException("shouldn't be any blobs in the container");
            };
            strategy.Execute();

            TestBlobClient.WriteBlob(account, containerName, "foo1.csv", "abc");

            int count = 0;
            executor.ExecuteLambda = (blob) =>
            {
                count++;
                Assert.Equal("foo1.csv", blob.Name);
                return true;
            };
            strategy.Execute();
            Assert.Equal(1, count);

            // Now run again; shouldn't show up. 
            executor.ExecuteLambda = (_) =>
            {
                throw new InvalidOperationException("shouldn't retrigger the same blob");
            };
            strategy.Execute();
        }

        private class LambdaBlobTriggerExecutor : ITriggerExecutor<ICloudBlob>
        {
            public Func<ICloudBlob, bool> ExecuteLambda { get; set; }

            public bool Execute(ICloudBlob value)
            {
                return ExecuteLambda.Invoke(value);
            }
        }
    }
}
