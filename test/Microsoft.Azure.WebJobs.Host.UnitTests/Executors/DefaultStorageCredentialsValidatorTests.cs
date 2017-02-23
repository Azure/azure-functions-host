using System;
using System.Net;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Shared.Protocol;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class DefaultStorageCredentialsValidatorTests
    {
        [Fact]
        public void StorageAccount_GetPropertiesThrows_InvalidCredentials()
        {
            var validator = new DefaultStorageCredentialsValidator();

            var storageMock = new Mock<IStorageAccount>(MockBehavior.Strict);
            var blobClientMock = new Mock<IStorageBlobClient>();
            var queueClientMock = new Mock<IStorageQueueClient>();
            var queueMock = new Mock<IStorageQueue>();

            storageMock.Setup(s => s.Credentials)
                .Returns(new StorageCredentials("name", new byte[] { }));

            storageMock.Setup(s => s.CreateBlobClient(null))
                .Returns(blobClientMock.Object)
                .Verifiable();

            blobClientMock.Setup(b => b.GetServicePropertiesAsync(It.IsAny<CancellationToken>()))
                .Throws(new StorageException(""));

            var exception = Assert.Throws<InvalidOperationException>(() => validator.ValidateCredentialsAsync(storageMock.Object, It.IsAny<CancellationToken>()).GetAwaiter().GetResult());
            Assert.Equal("Invalid storage account 'name'. Please make sure your credentials are correct.", exception.Message);
            storageMock.Verify();
        }

        [Fact]
        public void StorageAccount_QueueCheckThrows_BlobOnly()
        {
            var validator = new DefaultStorageCredentialsValidator();

            var storageMock = new Mock<IStorageAccount>(MockBehavior.Strict);
            var blobClientMock = new Mock<IStorageBlobClient>();
            var queueClientMock = new Mock<IStorageQueueClient>();
            var queueMock = new Mock<IStorageQueue>();

            storageMock.Setup(s => s.Credentials)
                .Returns(new StorageCredentials());

            storageMock.Setup(s => s.CreateBlobClient(null))
                .Returns(blobClientMock.Object)
                .Verifiable();

            blobClientMock.Setup(b => b.GetServicePropertiesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((ServiceProperties)null);

            storageMock.Setup(s => s.CreateQueueClient(null))
                .Returns(queueClientMock.Object)
                .Verifiable();

            queueClientMock.Setup(q => q.GetQueueReference(It.IsAny<string>()))
                .Returns(queueMock.Object);

            queueMock.Setup(q => q.ExistsAsync(It.IsAny<CancellationToken>()))
                .Throws(new StorageException("", new WebException("Remote name could not be resolved", WebExceptionStatus.NameResolutionFailure)));

            storageMock.SetupSet(s => s.Type = StorageAccountType.BlobOnly);

            validator.ValidateCredentialsAsync(storageMock.Object, It.IsAny<CancellationToken>()).GetAwaiter().GetResult();

            storageMock.Verify();
        }

        [Fact]
        public void StorageAccount_QueueCheckThrowsUnexpectedStorage()
        {
            var validator = new DefaultStorageCredentialsValidator();

            var storageMock = new Mock<IStorageAccount>(MockBehavior.Strict);
            var blobClientMock = new Mock<IStorageBlobClient>();
            var queueClientMock = new Mock<IStorageQueueClient>();
            var queueMock = new Mock<IStorageQueue>();

            storageMock.Setup(s => s.Credentials)
                .Returns(new StorageCredentials());

            storageMock.Setup(s => s.CreateBlobClient(null))
                .Returns(blobClientMock.Object)
                .Verifiable();

            blobClientMock.Setup(b => b.GetServicePropertiesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((ServiceProperties)null);

            storageMock.Setup(s => s.CreateQueueClient(null))
                .Returns(queueClientMock.Object)
                .Verifiable();

            queueClientMock.Setup(q => q.GetQueueReference(It.IsAny<string>()))
                .Returns(queueMock.Object);

            queueMock.Setup(q => q.ExistsAsync(It.IsAny<CancellationToken>()))
                .Throws(new StorageException("some other storage exception", null));

            var storageException = Assert.Throws<StorageException>(() => validator.ValidateCredentialsAsync(storageMock.Object, It.IsAny<CancellationToken>()).GetAwaiter().GetResult());
            Assert.Equal("some other storage exception", storageException.Message);
            storageMock.Verify();
        }
    }
}
