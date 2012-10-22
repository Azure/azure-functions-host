using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;

namespace OrchestratorUnitTests
{
    [TestClass]
    public class BlobListenerTests
    {
        [TestMethod]
        public void TestBlobListener()
        {
            var account = TestStorage.GetAccount();
            string containerName = @"daas-test-input";
            Utility.DeleteContainer(account, containerName);

            CloudBlobClient client = account.CreateCloudBlobClient();
            var container = client.GetContainerReference(containerName);
            BlobListener l = new BlobListener(new CloudBlobContainer[] { container });

            l.Poll(blob =>
                {
                    Assert.Fail("shouldn't be any blobs in the container");
                });

            Utility.WriteBlob(account, containerName, "foo1.csv", "abc");

            int count = 0;
            l.Poll(blob =>
            {
                count++;
                Assert.AreEqual("foo1.csv", blob.Name);
            });
            Assert.AreEqual(1, count);

            // No poll again, shouldn't show up. 
            l.Poll(blob =>
            {
                Assert.Fail("shouldn't retrigger the same blob");
            });            
        }
    }
}