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
            string name = Guid.NewGuid().ToString();
            string blobContents = "My Test Blob";
            CloudBlobContainer inputContainer = Fixture.BlobClient.GetContainerReference("test-input-bash");
            CloudBlockBlob inputBlob = inputContainer.GetBlockBlobReference(name);
            await inputBlob.UploadTextAsync(blobContents);

            CloudBlobContainer outputContainer = Fixture.BlobClient.GetContainerReference("test-output-bash");
            var resultBlob = outputContainer.GetBlockBlobReference(name);
            await TestHelpers.WaitForBlobAsync(resultBlob);

            string resultContents = resultBlob.DownloadText();
            Assert.Equal(blobContents, resultContents.Trim());
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Bash", "bash")
            {
            }
        }
    }
}
