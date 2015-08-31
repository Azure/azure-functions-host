using System;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Blobs.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Blobs.Bindings
{
    public class BlobContainerBindingTests
    {
        [Theory]
        [InlineData("container/blob", "container", "blob")]
        [InlineData("container/sub1/sub2/blob", "container", "sub1/sub2/blob")]
        [InlineData("container/sub1/sub2", "container", "sub1/sub2")]
        [InlineData("container", "container", "")]
        public void TryConvert_ConvertString_Success(string value, string expectedContainerValue, string expectedBlobValue)
        {
            Mock<IStorageBlobClient> mockStorageClient = new Mock<IStorageBlobClient>(MockBehavior.Strict);
            Mock<IStorageBlobContainer> mockStorageContainer = new Mock<IStorageBlobContainer>(MockBehavior.Strict);
            mockStorageClient.Setup(p => p.GetContainerReference(expectedContainerValue)).Returns(mockStorageContainer.Object);

            IStorageBlobContainer container = null;
            BlobPath path = null;
            bool result = BlobContainerBinding.TryConvert(value, mockStorageClient.Object, out container, out path);
            Assert.True(result);
            Assert.Equal(expectedContainerValue, path.ContainerName);
            Assert.Equal(expectedBlobValue, path.BlobName);

            mockStorageClient.VerifyAll();
        }

        [Theory]
        [InlineData("")]
        [InlineData("/")]
        [InlineData("/container/")]
        public void TryConvert_ConvertString_Failure(string value)
        {
            Mock<IStorageBlobClient> mockStorageClient = new Mock<IStorageBlobClient>(MockBehavior.Strict);

            IStorageBlobContainer container = null;
            BlobPath path = null;
            Assert.Throws<FormatException>(() =>
            {
                BlobContainerBinding.TryConvert(value, mockStorageClient.Object, out container, out path);
            });
        }
    }
}
