using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Moq;
using Xunit;
using Xunit.Extensions;
using System.Net;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class DefaultStorageCredentialsValidatorTests
    {
        [Fact]
        public void SecondaryStorageAccount_NoQueueCheck()
        {
            var validator = new DefaultStorageCredentialsValidator();

            var storageMock = new Mock<IStorageAccount>(MockBehavior.Strict);
            var blobClientMock = new Mock<IStorageBlobClient>();

            storageMock.Setup(s => s.Credentials)
                .Returns(new StorageCredentials());

            storageMock.Setup(s => s.CreateBlobClient(null))
                .Returns(blobClientMock.Object)
                .Verifiable();

            blobClientMock.Setup(b => b.GetServicePropertiesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(null);

            validator.ValidateCredentialsAsync(storageMock.Object, false, It.IsAny<CancellationToken>()).GetAwaiter().GetResult();

            storageMock.Verify();
        }

        [Fact]
        public void PrimaryStorageAccount_QueueCheckThrows()
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
                .ReturnsAsync(null);

            storageMock.Setup(s => s.CreateQueueClient(null))
                .Returns(queueClientMock.Object)
                .Verifiable();

            queueClientMock.Setup(q => q.GetQueueReference(It.IsAny<string>()))
                .Returns(queueMock.Object);

            queueMock.Setup(q => q.ExistsAsync(It.IsAny<CancellationToken>()))
                .Throws(new StorageException("", new WebException("Remote name could not be resolved", WebExceptionStatus.NameResolutionFailure)));

            ExceptionAssert.ThrowsInvalidOperation(() => validator.ValidateCredentialsAsync(storageMock.Object, true, It.IsAny<CancellationToken>())
                    .GetAwaiter().GetResult(), "Invalid storage account ''. Primary storage accounts must be general purpose accounts and not restricted blob storage accounts."
                );

            storageMock.Verify();
        }

        [Fact]
        public void PrimaryStorageAccount_QueueCheckThrowsUnexpectedStorage()
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
                .ReturnsAsync(null);

            storageMock.Setup(s => s.CreateQueueClient(null))
                .Returns(queueClientMock.Object)
                .Verifiable();

            queueClientMock.Setup(q => q.GetQueueReference(It.IsAny<string>()))
                .Returns(queueMock.Object);

            queueMock.Setup(q => q.ExistsAsync(It.IsAny<CancellationToken>()))
                .Throws(new StorageException("some other storage exception", null));

            var storageException = Assert.Throws<StorageException>(() => validator.ValidateCredentialsAsync(storageMock.Object, true, It.IsAny<CancellationToken>()).GetAwaiter().GetResult());
            Assert.Equal("some other storage exception", storageException.Message);
            storageMock.Verify();
        }

        [Fact]
        public void PrimaryStorageAccount_AlreadySet_SkipVerification()
        {
            var validator = new DefaultStorageCredentialsValidator();

            var storageMock = new Mock<IStorageAccount>(MockBehavior.Strict);

            storageMock.Setup(s => s.Credentials)
                .Returns((StorageCredentials)null);

            validator.ValidateCredentialsAsync(storageMock.Object, true, It.IsAny<CancellationToken>()).GetAwaiter().GetResult();

            storageMock.Verify();
        }
    }
}
