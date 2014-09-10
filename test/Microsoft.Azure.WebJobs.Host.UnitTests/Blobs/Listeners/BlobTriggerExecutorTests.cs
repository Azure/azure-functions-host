// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Blobs.Listeners;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Blobs.Listeners
{
    public class BlobTriggerExecutorTests
    {
        // Note: The tests that return true consume the notification.
        // The tests that return false reset the notification (to be provided again later).

        [Fact]
        public void ExecuteAsync_IfBlobDoesNotMatchPattern_ReturnsTrue()
        {
            // Arrange
            CloudStorageAccount account = CreateAccount();
            CloudBlobClient client = account.CreateCloudBlobClient();
            string containerName = "container";
            CloudBlobContainer container = client.GetContainerReference(containerName);
            CloudBlobContainer otherContainer = client.GetContainerReference("other");

            IBlobPathSource input = BlobPathSource.Create(containerName + "/{name}");

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input);

            ICloudBlob blob = otherContainer.GetBlockBlobReference("nonmatch");

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            Assert.True(task.Result);
        }

        [Fact]
        public void ExecuteAsync_IfBlobDoesNotExist_ReturnsTrue()
        {
            // Arrange
            CloudBlockBlob blob = CreateBlobReference();
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader(null);

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input, eTagReader);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            Assert.True(task.Result);
        }

        [Fact]
        public void ExecuteAsync_IfCompletedBlobReceiptExists_ReturnsTrue()
        {
            // Arrange
            CloudBlockBlob blob = CreateBlobReference();
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader("ETag");
            IBlobReceiptManager receiptManager = CreateCompletedReceiptManager();

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input, eTagReader, receiptManager);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            Assert.True(task.Result);
        }

        [Fact]
        public void ExecuteAsync_IfIncompleteBlobReceiptExists_TriesToAcquireLease()
        {
            // Arrange
            CloudBlockBlob blob = CreateBlobReference();
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader("ETag");

            Mock<IBlobReceiptManager> mock = CreateReceiptManagerReferenceMock();
            mock.Setup(m => m.TryReadAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(BlobReceipt.Incomplete));
            mock.Setup(m => m.TryAcquireLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<string>(null))
                .Verifiable();
            IBlobReceiptManager receiptManager = mock.Object;

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input, eTagReader, receiptManager);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            task.GetAwaiter().GetResult();
            mock.Verify();
        }

        [Fact]
        public void ExecuteAsync_IfBlobReceiptDoesNotExist_TriesToCreateReceipt()
        {
            // Arrange
            CloudBlockBlob blob = CreateBlobReference();
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader("ETag");

            Mock<IBlobReceiptManager> mock = CreateReceiptManagerReferenceMock();
            mock.Setup(m => m.TryReadAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<BlobReceipt>(null));
            mock.Setup(m => m.TryCreateAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(false))
                .Verifiable();
            IBlobReceiptManager receiptManager = mock.Object;

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input, eTagReader, receiptManager);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            task.GetAwaiter().GetResult();
            mock.Verify();
        }

        [Fact]
        public void ExecuteAsync_IfTryCreateReceiptFails_ReturnsFalse()
        {
            // Arrange
            CloudBlockBlob blob = CreateBlobReference();
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader("ETag");

            Mock<IBlobReceiptManager> mock = CreateReceiptManagerReferenceMock();
            mock.Setup(m => m.TryReadAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<BlobReceipt>(null));
            mock.Setup(m => m.TryCreateAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(false));
            IBlobReceiptManager receiptManager = mock.Object;

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input, eTagReader, receiptManager);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            Assert.False(task.Result);
        }

        [Fact]
        public void ExecuteAsync_IfTryCreateReceiptSucceeds_TriesToAcquireLease()
        {
            // Arrange
            CloudBlockBlob blob = CreateBlobReference();
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader("ETag");

            Mock<IBlobReceiptManager> mock = CreateReceiptManagerReferenceMock();
            mock.Setup(m => m.TryReadAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<BlobReceipt>(null));
            mock.Setup(m => m.TryCreateAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(true));
            mock.Setup(m => m.TryAcquireLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<string>(null))
                .Verifiable();
            IBlobReceiptManager receiptManager = mock.Object;

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input, eTagReader, receiptManager);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            task.GetAwaiter().GetResult();
            mock.Verify();
        }

        [Fact]
        public void ExecuteAsync_IfTryAcquireLeaseFails_ReturnsFalse()
        {
            // Arrange
            CloudBlockBlob blob = CreateBlobReference();
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader("ETag");

            Mock<IBlobReceiptManager> mock = CreateReceiptManagerReferenceMock();
            mock.Setup(m => m.TryReadAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(BlobReceipt.Incomplete));
            mock.Setup(m => m.TryAcquireLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<string>(null));
            IBlobReceiptManager receiptManager = mock.Object;

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input, eTagReader, receiptManager);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            Assert.False(task.Result);
        }

        [Fact]
        public void ExecuteAsync_IfTryAcquireLeaseSucceeds_ReadsLatestReceipt()
        {
            // Arrange
            CloudBlockBlob blob = CreateBlobReference();
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader("ETag");

            Mock<IBlobReceiptManager> mock = CreateReceiptManagerReferenceMock();
            int calls = 0;
            mock.Setup(m => m.TryReadAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                    {
                        return Task.FromResult(calls++ == 0 ? BlobReceipt.Incomplete : BlobReceipt.Complete);
                    });
            mock.Setup(m => m.TryAcquireLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult("LeaseId"));
            mock.Setup(m => m.ReleaseLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0));
            IBlobReceiptManager receiptManager = mock.Object;

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input, eTagReader, receiptManager);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            task.GetAwaiter().GetResult();
            Assert.Equal(2, calls);
        }

        [Fact]
        public void ExecuteAsync_IfLeasedReceiptBecameCompleted_ReleasesLeaseAndReturnsTrue()
        {
            // Arrange
            CloudBlockBlob blob = CreateBlobReference();
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader("ETag");

            Mock<IBlobReceiptManager> mock = CreateReceiptManagerReferenceMock();
            int calls = 0;
            mock.Setup(m => m.TryReadAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    int call = calls++;
                    return Task.FromResult(call == 0 ? BlobReceipt.Incomplete : BlobReceipt.Complete);
                });
            mock.Setup(m => m.TryAcquireLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult("LeaseId"));
            mock.Setup(m => m.ReleaseLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0))
                .Verifiable();
            IBlobReceiptManager receiptManager = mock.Object;

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input, eTagReader, receiptManager);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            task.WaitUntilCompleted();
            mock.Verify();
            Assert.True(task.Result);
        }

        [Fact]
        public void ExecuteAsync_IfEnqueueAsyncThrows_ReleasesLease()
        {
            // Arrange
            CloudBlockBlob blob = CreateBlobReference();
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader("ETag");
            InvalidOperationException expectedException = new InvalidOperationException();

            Mock<IBlobReceiptManager> mock = CreateReceiptManagerReferenceMock();
            mock.Setup(m => m.TryReadAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(BlobReceipt.Incomplete));
            mock.Setup(m => m.TryAcquireLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult("LeaseId"));
            mock.Setup(m => m.ReleaseLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0))
                .Verifiable();
            IBlobReceiptManager receiptManager = mock.Object;

            Mock<IBlobTriggerQueueWriter> queueWriterMock = new Mock<IBlobTriggerQueueWriter>(MockBehavior.Strict);
            queueWriterMock
                .Setup(w => w.EnqueueAsync(It.IsAny<BlobTriggerMessage>(), It.IsAny<CancellationToken>()))
                .Throws(expectedException);
            IBlobTriggerQueueWriter queueWriter = queueWriterMock.Object;

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(input, eTagReader, receiptManager,
                queueWriter);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            task.WaitUntilCompleted();
            mock.Verify();
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => task.GetAwaiter().GetResult());
            Assert.Same(expectedException, exception);
        }

        [Fact]
        public void ExecuteAsync_IfLeasedIncompleteReceipt_EnqueuesMessageMarksCompletedReleasesLeaseAndReturnsTrue()
        {
            // Arrange
            string expectedFunctionId = "FunctionId";
            string expectedETag = "ETag";
            CloudBlockBlob blob = CreateBlobReference("container", "blob");
            IBlobPathSource input = CreateBlobPath(blob);
            IBlobETagReader eTagReader = CreateStubETagReader(expectedETag);

            Mock<IBlobReceiptManager> managerMock = CreateReceiptManagerReferenceMock();
            managerMock
                .Setup(m => m.TryReadAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(BlobReceipt.Incomplete));
            managerMock
                .Setup(m => m.TryAcquireLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult("LeaseId"));
            managerMock
                .Setup(m => m.MarkCompletedAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0))
                .Verifiable();
            managerMock
                .Setup(m => m.ReleaseLeaseAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0))
                .Verifiable();
            IBlobReceiptManager receiptManager = managerMock.Object;

            Mock<IBlobTriggerQueueWriter> queueWriterMock = new Mock<IBlobTriggerQueueWriter>(MockBehavior.Strict);
            queueWriterMock
                .Setup(w => w.EnqueueAsync(It.IsAny<BlobTriggerMessage>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(0));
            IBlobTriggerQueueWriter queueWriter = queueWriterMock.Object;

            ITriggerExecutor<ICloudBlob> product = CreateProductUnderTest(expectedFunctionId, input, eTagReader,
                receiptManager, queueWriter);

            // Act
            Task<bool> task = product.ExecuteAsync(blob, CancellationToken.None);

            // Assert
            task.WaitUntilCompleted();
            queueWriterMock
                .Verify(
                    w => w.EnqueueAsync(It.Is<BlobTriggerMessage>(m =>
                        m != null && m.FunctionId == expectedFunctionId && m.BlobType == BlobType.BlockBlob &&
                        m.BlobName == blob.Name && m.ContainerName == blob.Container.Name && m.ETag == expectedETag),
                        It.IsAny<CancellationToken>()),
                    Times.Once());
            managerMock.Verify();
            Assert.True(task.Result);
        }

        private static CloudStorageAccount CreateAccount()
        {
            return CloudStorageAccount.DevelopmentStorageAccount;
        }

        private static CloudBlockBlob CreateBlobReference()
        {
            return CreateBlobReference("container", "blob");
        }

        private static CloudBlockBlob CreateBlobReference(string containerName, string blobName)
        {
            CloudStorageAccount account = CreateAccount();
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(containerName);
            return container.GetBlockBlobReference(blobName);
        }

        private static IBlobPathSource CreateBlobPath(ICloudBlob blob)
        {
            return new FixedBlobPathSource(blob.ToBlobPath());
        }

        private static IBlobReceiptManager CreateCompletedReceiptManager()
        {
            Mock<IBlobReceiptManager> mock = CreateReceiptManagerReferenceMock();
            mock.Setup(m => m.TryReadAsync(It.IsAny<CloudBlockBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(BlobReceipt.Complete));
            return mock.Object;
        }

        private static IBlobETagReader CreateDummyETagReader()
        {
            return new Mock<IBlobETagReader>(MockBehavior.Strict).Object;
        }

        private static IBlobReceiptManager CreateDummyReceiptManager()
        {
            return new Mock<IBlobReceiptManager>(MockBehavior.Strict).Object;
        }

        private static IBlobTriggerQueueWriter CreateDummyQueueWriter()
        {
            return new Mock<IBlobTriggerQueueWriter>(MockBehavior.Strict).Object;
        }

        private static BlobTriggerExecutor CreateProductUnderTest(IBlobPathSource input)
        {
            return CreateProductUnderTest(input, CreateDummyETagReader());
        }

        private static BlobTriggerExecutor CreateProductUnderTest(IBlobPathSource input, IBlobETagReader eTagReader)
        {
            return CreateProductUnderTest(input, eTagReader, CreateDummyReceiptManager());
        }

        private static BlobTriggerExecutor CreateProductUnderTest(IBlobPathSource input, IBlobETagReader eTagReader,
            IBlobReceiptManager receiptManager)
        {
            return CreateProductUnderTest("FunctionId", input, eTagReader, receiptManager, CreateDummyQueueWriter());
        }

        private static BlobTriggerExecutor CreateProductUnderTest(IBlobPathSource input, IBlobETagReader eTagReader,
            IBlobReceiptManager receiptManager, IBlobTriggerQueueWriter queueWriter)
        {
            return CreateProductUnderTest("FunctionId", input, eTagReader, receiptManager, queueWriter);
        }

        private static BlobTriggerExecutor CreateProductUnderTest(string functionId, IBlobPathSource input,
            IBlobETagReader eTagReader, IBlobReceiptManager receiptManager, IBlobTriggerQueueWriter queueWriter)
        {
            return new BlobTriggerExecutor(String.Empty, functionId, input, eTagReader, receiptManager, queueWriter);
        }

        private static Mock<IBlobReceiptManager> CreateReceiptManagerReferenceMock()
        {
            CloudBlockBlob receiptBlob = CreateAccount().CreateCloudBlobClient()
                .GetContainerReference("receipts").GetBlockBlobReference("item");
            Mock<IBlobReceiptManager> mock = new Mock<IBlobReceiptManager>(MockBehavior.Strict);
            mock.Setup(m => m.CreateReference(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>()))
                .Returns(receiptBlob);
            return mock;
        }

        private static IBlobETagReader CreateStubETagReader(string eTag)
        {
            Mock<IBlobETagReader> mock = new Mock<IBlobETagReader>(MockBehavior.Strict);
            mock
                .Setup(r => r.GetETagAsync(It.IsAny<ICloudBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(eTag));
            return mock.Object;
        }
    }
}
