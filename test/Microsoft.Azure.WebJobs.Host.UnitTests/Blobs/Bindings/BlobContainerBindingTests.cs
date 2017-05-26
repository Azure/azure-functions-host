// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Blobs.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;
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

        [Fact]
        public void ToParameterDescriptor_ReturnsExpectedDescriptor()
        {
            Mock<IArgumentBinding<IStorageBlobContainer>> mockArgumentBinding = new Mock<IArgumentBinding<IStorageBlobContainer>>(MockBehavior.Strict);
            Mock<IStorageBlobClient> mockStorageClient = new Mock<IStorageBlobClient>(MockBehavior.Strict);
            Mock<IBindableBlobPath> mockBlobPath = new Mock<IBindableBlobPath>(MockBehavior.Strict);
            BlobContainerBinding binding = new BlobContainerBinding("testparam", mockArgumentBinding.Object, mockStorageClient.Object, mockBlobPath.Object);
            ParameterDescriptor descriptor = binding.ToParameterDescriptor();
            Assert.Equal(typeof(ParameterDescriptor), descriptor.GetType());
            Assert.Equal("testparam", descriptor.Name);
            Assert.Equal("Enter the blob container name or blob path prefix", descriptor.DisplayHints.Prompt);
        }

        [Fact]
        public void ValidateContainerBinding_PerformsExpectedValidations()
        {
            BlobAttribute attribute = new BlobAttribute("test/blob", FileAccess.Write);
            Mock<IBindableBlobPath> mockPath = new Mock<IBindableBlobPath>(MockBehavior.Strict);
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                BlobContainerBinding.ValidateContainerBinding(attribute, typeof(IEnumerable<CloudBlockBlob>), mockPath.Object);
            });
            Assert.Equal("Only the 'Read' FileAccess mode is supported for blob container bindings.", ex.Message);

            attribute = new BlobAttribute("test/blob", FileAccess.Read);
            mockPath.Setup(p => p.BlobNamePattern).Returns("prefix");
            ex = Assert.Throws<InvalidOperationException>(() =>
            {
                BlobContainerBinding.ValidateContainerBinding(attribute, typeof(CloudBlobContainer), mockPath.Object);
            });
            Assert.Equal("Only a container name can be specified when binding to CloudBlobContainer.", ex.Message);
        }
    }
}
