// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PhpEndToEndTests : EndToEndTestsBase<PhpEndToEndTests.TestFixture>
    {
        public PhpEndToEndTests(TestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task BlobTriggerToBlobTest()
        {
            // the trigger blob was written by the fixture init code
            // here we just wait for the output blob
            CloudBlobContainer outputContainer = Fixture.BlobClient.GetContainerReference("test-output-php");
            var resultBlob = outputContainer.GetBlockBlobReference(Fixture.TestBlobName);
            await TestHelpers.WaitForBlobAsync(resultBlob);

            string resultContents = resultBlob.DownloadText();
            Assert.Equal(Fixture.TestBlobTriggerContents + "_" + Fixture.TestBlobContents, resultContents.Trim());
        }

        [Fact]
        public async Task ManualTrigger_Invoke_Succeeds()
        {
            await ManualTrigger_Invoke_SucceedsTest();
        }

        [Fact]
        public async Task QueueTriggerToBlob()
        {
            await QueueTriggerToBlobTest();
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Php", "php")
            {
            }

            public string TestBlobTriggerContents { get; private set; }

            public string TestBlobContents { get; private set; }

            public string TestBlobName { get; private set; }

            public string TestBlobTriggerName { get; private set; }

            protected override void CreateTestStorageEntities()
            {
                // This will ensure the input container is created.
                base.CreateTestStorageEntities();

                TestBlobTriggerContents = "My Test Blob Trigger";
                TestBlobContents = "My Test Blob";
                TestBlobTriggerName = "testBlobTrigger";
                TestBlobName = "testBlob";

                // write the test blob before the host starts, so it gets picked
                // up relatively quickly by the blob trigger test               
                CloudBlockBlob inputBlobTrigger = TestInputContainer.GetBlockBlobReference(TestBlobTriggerName);
                inputBlobTrigger.UploadText(TestBlobTriggerContents);

                CloudBlockBlob inputBlob = TestInputContainer.GetBlockBlobReference(TestBlobName);
                inputBlob.UploadText(TestBlobContents);
            }
        }
    }
}
