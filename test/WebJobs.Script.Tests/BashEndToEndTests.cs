// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class BashEndToEndTests : EndToEndTestsBase<BashEndToEndTests.TestFixture>
    {
        public BashEndToEndTests(TestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task BlobTriggerToBlobTest()
        {
            // the trigger blob was written by the fixture init code
            // here we just wait for the output blob
            CloudBlobContainer outputContainer = Fixture.BlobClient.GetContainerReference("test-output-bash");
            var resultBlob = outputContainer.GetBlockBlobReference(Fixture.TestBlobName);
            await TestHelpers.WaitForBlobAsync(resultBlob);

            string resultContents = resultBlob.DownloadText();
            Assert.Equal(Fixture.TestBlobContents, resultContents.Trim());
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Bash", "bash")
            {
            }

            public string TestBlobContents { get; private set; }

            public string TestBlobName { get; private set; }

            protected override void CreateTestStorageEntities()
            {
                // This will ensure the input container is created.
                base.CreateTestStorageEntities();

                TestBlobContents = "My Test Blob";
                TestBlobName = Guid.NewGuid().ToString();

                // write the test blob before the host starts, so it gets picked
                // up relatively quickly by the blob trigger test
                CloudBlockBlob inputBlob = TestInputContainer.GetBlockBlobReference(TestBlobName);
                inputBlob.UploadText(TestBlobContents);
            }
        }
    }
}
