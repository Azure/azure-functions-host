using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using TriggerService;


namespace TriggerServiceUnitTests
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
            IBlobListener l = new ContainerScannerBlobListener(new CloudBlobContainer[] { container });

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


        // Set dev storage. These are well known values.
        class TestStorage
        {
            public static CloudStorageAccount GetAccount()
            {
                return CloudStorageAccount.DevelopmentStorageAccount;
            }
        }
    }
}