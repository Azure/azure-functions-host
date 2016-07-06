// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.EndToEndTests
{
    public class BlobBindingEndToEndTests : IClassFixture<BlobBindingEndToEndTests.TestFixture>
    {
        private const string TestArtifactPrefix = "e2etestbindings";
        private const string ContainerName = TestArtifactPrefix + "-%rnd%";
        private const string OutputContainerName = TestArtifactPrefix + "-out%rnd%";
        private const string PageBlobContainerName = TestArtifactPrefix + "pageblobs-%rnd%";
        private const string HierarchicalBlobContainerName = TestArtifactPrefix + "subblobs-%rnd%";
        private const string TestData = "TestData";
        private readonly TestFixture _fixture;
        private static int NumBlobsRead;

        public BlobBindingEndToEndTests(TestFixture fixture)
        {
            _fixture = fixture;
            NumBlobsRead = 0;
        }

        [Fact]
        public async Task BindToCloudBlobContainer()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("CloudBlobContainerBinding"));

            Assert.Equal(5, NumBlobsRead);
        }

        [Fact]
        public async Task BindToCloudBlobDirectory()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("CloudBlobDirectoryBinding"));

            Assert.Equal(3, NumBlobsRead);
        }

        [Fact]
        public async Task BindToCloudBlobContainer_WithModelBinding()
        {
            TestPoco poco = new TestPoco
            {
                A = _fixture.Config.NameResolver.ResolveWholeString(ContainerName)
            };
            string json = JsonConvert.SerializeObject(poco);
            var arguments = new { poco = json };

            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("CloudBlobContainerBinding_WithModelBinding"), arguments);

            Assert.Equal(5, NumBlobsRead);
        }

        [Fact]
        public async Task BindToIEnumerableCloudBlockBlob_WithPrefixFilter()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("IEnumerableCloudBlockBlobBinding_WithPrefixFilter"));

            Assert.Equal(3, NumBlobsRead);
        }

        [Fact]
        public async Task BindToIEnumerableCloudBlockBlob_WithPrefixFilter_NoMatchingBlobs()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("IEnumerableCloudBlockBlobBinding_WithPrefixFilter_NoMatchingBlobs"));

            Assert.Equal(0, NumBlobsRead);
        }

        [Fact]
        public async Task BindToIEnumerableCloudBlockBlob_WithPrefixFilter_HierarchicalBlobs()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("IEnumerableCloudBlockBlobBinding_WithPrefixFilter_HierarchicalBlobs"));

            Assert.Equal(2, NumBlobsRead);
        }

        [Fact]
        public async Task BindToIEnumerableCloudBlockBlob_WithPrefixFilter_HierarchicalBlobs_UsesFlatBlobListing()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("IEnumerableCloudBlockBlobBinding_WithPrefixFilter_HierarchicalBlobs_UsesFlatBlobListing"));

            Assert.Equal(3, NumBlobsRead);
        }

        [Fact]
        public async Task BindToIEnumerableCloudBlockBlob_WithModelBinding()
        {
            TestPoco poco = new TestPoco
            {
                A = _fixture.Config.NameResolver.ResolveWholeString(ContainerName),
                B = "bl"
            };
            string json = JsonConvert.SerializeObject(poco);
            var arguments = new { poco = json };

            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("IEnumerableCloudBlockBlobBinding_WithModelBinding"), arguments);

            Assert.Equal(3, NumBlobsRead);
        }

        [Fact]
        public async Task BindToIEnumerableCloudPageBlob()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("IEnumerableCloudPageBlobBinding"));

            Assert.Equal(2, NumBlobsRead);
        }

        [Fact]
        public async Task BindToIEnumerableString()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("IEnumerableStringBinding"));

            Assert.Equal(5, NumBlobsRead);
        }

        [Fact]
        public async Task BindToIEnumerableStream()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("IEnumerableStreamBinding"));

            Assert.Equal(5, NumBlobsRead);
        }

        [Fact]
        public async Task BindToTextReader()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("IEnumerableTextReaderBinding"));

            Assert.Equal(5, NumBlobsRead);
        }

        [Fact]
        public async Task BindToICloudBlob()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("IEnumerableICloudBlobBinding"));

            Assert.Equal(5, NumBlobsRead);
        }

        [Fact]
        public async Task BindToByteArray()
        {
            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("ByteArrayBinding"));

            Assert.Equal(1, NumBlobsRead);
        }

        [Fact]
        public async Task BindToByteArray_Output()
        {
            // if the function sets the output binding to null, no blob
            // should be written
            var arguments = new { input = "null" };
            var method = typeof(BlobBindingEndToEndTests).GetMethod("ByteArrayOutputBinding");
            await _fixture.Host.CallAsync(method, arguments);

            CloudBlockBlob blob = _fixture.OutputBlobContainer.GetBlockBlobReference("blob1");
            Assert.False(blob.Exists());
           
            // if the function sets a value, the blob should be written
            arguments = new { input = TestData };
            await _fixture.Host.CallAsync(method, arguments);

            Assert.True(blob.Exists());
            string result = blob.DownloadText();
            Assert.Equal(TestData, result);
        }

        [Fact]
        public async Task BindToByteArray_Trigger()
        {
            var arguments = new { blob = string.Format("{0}/{1}", _fixture.Config.NameResolver.ResolveWholeString(ContainerName), "blob1") };

            await _fixture.Host.CallAsync(typeof(BlobBindingEndToEndTests).GetMethod("ByteArrayTriggerBinding"), arguments);

            Assert.Equal(1, NumBlobsRead);
        }

        [Fact]
        public void BlobTriggerSingletonListener_LockIsHeld()
        {
            _fixture.VerifyLockState("BlobTrigger.Listener", LeaseState.Leased, LeaseStatus.Locked);
        }

        // This function just exists to force initialization of the
        // blob listener pipeline
        public static void TestBlobTrigger([BlobTrigger("test/test")] string blob)
        {
        }

        [NoAutomaticTrigger]
        public static void CloudBlobContainerBinding(
            [Blob(ContainerName)] CloudBlobContainer container)
        {
            var blobs = container.ListBlobs();
            foreach (CloudBlockBlob blob in blobs)
            {
                string content = blob.DownloadText();
                Assert.Equal(TestData, content);
            }
            NumBlobsRead = blobs.Count();
        }

        [NoAutomaticTrigger]
        public static void CloudBlobDirectoryBinding(
            [Blob(HierarchicalBlobContainerName + "/sub")] CloudBlobDirectory directory)
        {
            var directoryItems = directory.ListBlobs();

            var blobs = directoryItems.OfType<CloudBlockBlob>();
            foreach (CloudBlockBlob blob in blobs)
            {
                string content = blob.DownloadText();
                Assert.Equal(TestData, content);
            }
            NumBlobsRead += blobs.Count();

            CloudBlobDirectory subDirectory = directoryItems.OfType<CloudBlobDirectory>().Single();
            CloudBlockBlob subBlob = subDirectory.ListBlobs().Cast<CloudBlockBlob>().Single();
            Assert.Equal(TestData, subBlob.DownloadText());
            NumBlobsRead += 1;
        }

        [NoAutomaticTrigger]
        public static void CloudBlobContainerBinding_WithModelBinding(
            [QueueTrigger("testqueue")] TestPoco poco,
            [Blob("{A}")] CloudBlobContainer container)
        {
            CloudBlobContainerBinding(container);
        }

        [NoAutomaticTrigger]
        public static void IEnumerableCloudBlockBlobBinding_WithPrefixFilter(
            [Blob(ContainerName + "/blo")] IEnumerable<CloudBlockBlob> blobs)
        {
            foreach (var blob in blobs)
            {
                string content = blob.DownloadText();
                Assert.Equal(TestData, content);
            }
            NumBlobsRead = blobs.Count();
        }

        [NoAutomaticTrigger]
        public static void IEnumerableCloudBlockBlobBinding_WithPrefixFilter_NoMatchingBlobs(
            [Blob(ContainerName + "/dne")] IEnumerable<CloudBlockBlob> blobs)
        {
            NumBlobsRead = blobs.Count();
        }

        [NoAutomaticTrigger]
        public static void IEnumerableCloudBlockBlobBinding_WithPrefixFilter_HierarchicalBlobs(
            [Blob(HierarchicalBlobContainerName + "/sub/bl")] IEnumerable<CloudBlockBlob> blobs)
        {
            foreach (var blob in blobs)
            {
                string content = blob.DownloadText();
                Assert.Equal(TestData, content);
            }
            NumBlobsRead = blobs.Count();
        }

        // Ensure that a flat blob listing is used, meaning if a route prefix covers
        // sub directries, blobs within those sub directories are returned. Users can bind
        // to CloudBlobDirectory if they want to operate on directories.
        [NoAutomaticTrigger]
        public static void IEnumerableCloudBlockBlobBinding_WithPrefixFilter_HierarchicalBlobs_UsesFlatBlobListing(
            [Blob(HierarchicalBlobContainerName + "/sub")] IEnumerable<CloudBlockBlob> blobs)
        {
            foreach (var blob in blobs)
            {
                string content = blob.DownloadText();
                Assert.Equal(TestData, content);
            }
            NumBlobsRead = blobs.Count();
        }

        [NoAutomaticTrigger]
        public static void IEnumerableCloudBlockBlobBinding_WithModelBinding(
            [QueueTrigger("testqueue")] TestPoco poco,
            [Blob("{A}/{B}ob")] IEnumerable<CloudBlockBlob> blobs)
        {
            foreach (var blob in blobs)
            {
                string content = blob.DownloadText();
                Assert.Equal(TestData, content);
            }
            NumBlobsRead = blobs.Count();
        }

        [NoAutomaticTrigger]
        public static void IEnumerableCloudPageBlobBinding(
            [Blob(PageBlobContainerName)] IEnumerable<CloudPageBlob> blobs)
        {
            foreach (var blob in blobs)
            {
                byte[] bytes = new byte[512];
                int byteCount = blob.DownloadToByteArray(bytes, 0);
                string content = Encoding.UTF8.GetString(bytes, 0, byteCount);
                Assert.True(content.StartsWith(TestData));
            }
            NumBlobsRead = blobs.Count();
        }

        [NoAutomaticTrigger]
        public static void IEnumerableStringBinding(
            [Blob(ContainerName)] IEnumerable<string> blobs)
        {
            foreach (var blob in blobs)
            {
                Assert.Equal(TestData, blob);
            }
            NumBlobsRead = blobs.Count();
        }

        [NoAutomaticTrigger]
        public static void IEnumerableStreamBinding(
            [Blob(ContainerName)] IEnumerable<Stream> blobs)
        {
            foreach (var blobStream in blobs)
            {
                using (StreamReader reader = new StreamReader(blobStream))
                {
                    string content = reader.ReadToEnd();
                    Assert.Equal(TestData, content);
                }
            }
            NumBlobsRead = blobs.Count();
        }

        [NoAutomaticTrigger]
        public static void IEnumerableTextReaderBinding(
            [Blob(ContainerName)] IEnumerable<TextReader> blobs)
        {
            foreach (var blob in blobs)
            {
                string content = blob.ReadToEnd();
                Assert.Equal(TestData, content);
            }
            NumBlobsRead = blobs.Count();
        }

        [NoAutomaticTrigger]
        public static async Task IEnumerableICloudBlobBinding(
            [Blob(ContainerName)] IEnumerable<ICloudBlob> blobs)
        {
            foreach (var blob in blobs)
            {
                Stream stream = await blob.OpenReadAsync();
                using (StreamReader reader = new StreamReader(stream))
                {
                    string content = reader.ReadToEnd();
                    Assert.Equal(TestData, content);
                }
            }
            NumBlobsRead = blobs.Count();
        }

        [NoAutomaticTrigger]
        public static void ByteArrayBinding(
            [Blob(ContainerName + "/blob1")] byte[] blob)
        {
            string result = Encoding.UTF8.GetString(blob);
            Assert.Equal(TestData, result);
            NumBlobsRead = 1;
        }

        [NoAutomaticTrigger]
        public static void ByteArrayOutputBinding(string input,
            [Blob(OutputContainerName + "/blob1")] out byte[] output)
        {
            if (input == "null")
            {
                output = null;
            }
            else
            {
                output = Encoding.UTF8.GetBytes(input);
            }
        }

        [NoAutomaticTrigger]
        public static void ByteArrayTriggerBinding(
            [BlobTrigger(ContainerName)] byte[] blob)
        {
            string result = Encoding.UTF8.GetString(blob);
            Assert.Equal(TestData, result);
            NumBlobsRead = 1;
        }

        public class TestFixture : IDisposable
        {
            public TestFixture()
            {
                RandomNameResolver nameResolver = new RandomNameResolver();
                JobHostConfiguration hostConfiguration = new JobHostConfiguration()
                {
                    NameResolver = nameResolver,
                    TypeLocator = new FakeTypeLocator(typeof(BlobBindingEndToEndTests)),
                };
                Config = hostConfiguration;

                StorageAccount = CloudStorageAccount.Parse(hostConfiguration.StorageConnectionString);
                CloudBlobClient blobClient = StorageAccount.CreateCloudBlobClient();

                BlobContainer = blobClient.GetContainerReference(nameResolver.ResolveInString(ContainerName));
                Assert.False(BlobContainer.Exists());
                BlobContainer.Create();

                OutputBlobContainer = blobClient.GetContainerReference(nameResolver.ResolveInString(OutputContainerName));

                CloudBlobContainer pageBlobContainer = blobClient.GetContainerReference(nameResolver.ResolveInString(PageBlobContainerName));
                Assert.False(pageBlobContainer.Exists());
                pageBlobContainer.Create();

                CloudBlobContainer hierarchicalBlobContainer = blobClient.GetContainerReference(nameResolver.ResolveInString(HierarchicalBlobContainerName));
                Assert.False(hierarchicalBlobContainer.Exists());
                hierarchicalBlobContainer.Create();

                Host = new JobHost(hostConfiguration);
                Host.Start();

                // upload some test blobs
                CloudBlockBlob blob = BlobContainer.GetBlockBlobReference("blob1");
                blob.UploadText(TestData);
                blob = BlobContainer.GetBlockBlobReference("blob2");
                blob.UploadText(TestData);
                blob = BlobContainer.GetBlockBlobReference("blob3");
                blob.UploadText(TestData);
                blob = BlobContainer.GetBlockBlobReference("file1");
                blob.UploadText(TestData);
                blob = BlobContainer.GetBlockBlobReference("file2");
                blob.UploadText(TestData);

                // add a couple hierarchical blob paths
                blob = hierarchicalBlobContainer.GetBlockBlobReference("sub/blob1");
                blob.UploadText(TestData);
                blob = hierarchicalBlobContainer.GetBlockBlobReference("sub/blob2");
                blob.UploadText(TestData);
                blob = hierarchicalBlobContainer.GetBlockBlobReference("sub/sub/blob3");
                blob.UploadText(TestData);
                blob = hierarchicalBlobContainer.GetBlockBlobReference("blob4");
                blob.UploadText(TestData);

                byte[] bytes = new byte[512];
                byte[] testBytes = Encoding.UTF8.GetBytes(TestData);
                for (int i = 0; i < testBytes.Length ; i++)
                {
                    bytes[i] = testBytes[i];
                }
                CloudPageBlob pageBlob = pageBlobContainer.GetPageBlobReference("blob1");
                pageBlob.UploadFromByteArray(bytes, 0, bytes.Length);
                pageBlob = pageBlobContainer.GetPageBlobReference("blob2");
                pageBlob.UploadFromByteArray(bytes, 0, bytes.Length);
            }

            public JobHost Host
            {
                get;
                private set;
            }

            public JobHostConfiguration Config
            {
                get;
                private set;
            }

            public CloudStorageAccount StorageAccount
            {
                get;
                private set;
            }

            public CloudBlobContainer BlobContainer
            {
                get;
                private set;
            }

            public CloudBlobContainer OutputBlobContainer
            {
                get;
                private set;
            }

            public void Dispose()
            {
                Host.Stop();

                VerifyLockState("BlobTrigger.Listener", LeaseState.Available, LeaseStatus.Unlocked);

                CloudBlobClient blobClient = StorageAccount.CreateCloudBlobClient();
                foreach (var testContainer in blobClient.ListContainers(TestArtifactPrefix))
                {
                    testContainer.Delete();
                }
            }

            public void VerifyLockState(string lockId, LeaseState state, LeaseStatus status)
            {
                CloudBlobClient blobClient = StorageAccount.CreateCloudBlobClient();
                var container = blobClient.GetContainerReference("azure-webjobs-hosts");
                string blobName = string.Format("locks/{0}/{1}", Config.HostId, lockId);
                var lockBlob = container.GetBlockBlobReference(blobName);

                Assert.True(lockBlob.Exists());
                lockBlob.FetchAttributes();

                Assert.Equal(state, lockBlob.Properties.LeaseState);
                Assert.Equal(status, lockBlob.Properties.LeaseStatus);
            }
        }

        public class TestPoco
        {
            public string A { get; set;}

            public string B { get; set; }
        }
    }
}
