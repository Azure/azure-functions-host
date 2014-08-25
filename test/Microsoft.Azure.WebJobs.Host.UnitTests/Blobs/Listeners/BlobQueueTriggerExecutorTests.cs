// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Blobs.Listeners;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Moq;
using Newtonsoft.Json;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Blobs.Listeners
{
    public class BlobQueueTriggerExecutorTests
    {
        [Fact]
        public void ExecuteAsync_IfMessageIsNotJson_Throws()
        {
            // Arrange
            BlobQueueTriggerExecutor product = CreateProductUnderTest();
            CloudQueueMessage message = CreateMessage("ThisIsNotValidJson");

            // Act
            Task task = product.ExecuteAsync(message, CancellationToken.None);

            // Assert
            Assert.Throws<JsonReaderException>(() => task.GetAwaiter().GetResult());
        }

        [Fact]
        public void ExecuteAsync_IfMessageIsJsonNull_Throws()
        {
            // Arrange
            BlobQueueTriggerExecutor product = CreateProductUnderTest();
            CloudQueueMessage message = CreateMessage("null");

            // Act
            Task task = product.ExecuteAsync(message, CancellationToken.None);

            // Assert
            ExceptionAssert.ThrowsInvalidOperation(() => task.GetAwaiter().GetResult(),
                "Invalid blob trigger message.");
        }

        [Fact]
        public void ExecuteAsync_IfFunctionIdIsNull_Throws()
        {
            // Arrange
            BlobQueueTriggerExecutor product = CreateProductUnderTest();
            CloudQueueMessage message = CreateMessage("{}");

            // Act
            Task task = product.ExecuteAsync(message, CancellationToken.None);

            // Assert
            ExceptionAssert.ThrowsInvalidOperation(() => task.GetAwaiter().GetResult(), "Invalid function ID.");
        }

        [Fact]
        public void ExecuteAsync_IfMessageIsFunctionIdIsNotRegistered_ReturnsTrue()
        {
            // Arrange
            BlobQueueTriggerExecutor product = CreateProductUnderTest();
            CloudQueueMessage message = CreateMessage(new BlobTriggerMessage { FunctionId = "Missing" });

            // Act
            Task<bool> task = product.ExecuteAsync(message, CancellationToken.None);

            // Assert
            Assert.True(task.Result);
        }

        [Theory]
        [InlineData(BlobType.BlockBlob)]
        [InlineData(BlobType.PageBlob)]
        public void ExecuteAsync_IfMessageIsFunctionIdIsRegistered_GetsETag(BlobType expectedBlobType)
        {
            // Arrange
            string expectedContainerName = "container";
            string expectedBlobName = "blob";
            string functionId = "FunctionId";
            Mock<IBlobETagReader> mock = new Mock<IBlobETagReader>(MockBehavior.Strict);
            mock.Setup(r => r.GetETagAsync(It.Is<ICloudBlob>(b => b.BlobType == expectedBlobType &&
                    b.Name == expectedBlobName && b.Container.Name == expectedContainerName),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult("ETag"))
                .Verifiable();
            IBlobETagReader eTagReader = mock.Object;
            BlobQueueTriggerExecutor product = CreateProductUnderTest(eTagReader);

            product.Register(functionId, CreateDummyInstanceFactory());

            BlobTriggerMessage triggerMessage = new BlobTriggerMessage
            {
                FunctionId = functionId,
                BlobType = expectedBlobType,
                ContainerName = expectedContainerName,
                BlobName = expectedBlobName,
                ETag = "OriginalETag"

            };
            CloudQueueMessage message = CreateMessage(triggerMessage);

            // Act
            Task task = product.ExecuteAsync(message, CancellationToken.None);

            // Assert
            task.WaitUntilCompleted();
            mock.Verify();
        }

        [Fact]
        public void ExecuteAsync_IfBlobHasBeenDeleted_ReturnsTrue()
        {
            // Arrange
            string functionId = "FunctionId";
            IBlobETagReader eTagReader = CreateStubETagReader(null);
            BlobQueueTriggerExecutor product = CreateProductUnderTest(eTagReader);

            product.Register(functionId, CreateDummyInstanceFactory());

            CloudQueueMessage message = CreateMessage(functionId, "OriginalETag");

            // Act
            Task<bool> task = product.ExecuteAsync(message, CancellationToken.None);

            // Assert
            Assert.True(task.Result);
        }

        [Fact]
        public void ExecuteAsync_IfBlobHasChanged_NotifiesWatcherAndReturnsTrue()
        {
            // Arrange
            string functionId = "FunctionId";
            IBlobETagReader eTagReader = CreateStubETagReader("NewETag");
            Mock<IBlobWrittenWatcher> mock = new Mock<IBlobWrittenWatcher>(MockBehavior.Strict);
            mock.Setup(w => w.Notify(It.IsAny<ICloudBlob>()))
                .Verifiable();
            IBlobWrittenWatcher blobWrittenWatcher = mock.Object;
            BlobQueueTriggerExecutor product = CreateProductUnderTest(eTagReader, blobWrittenWatcher);

            product.Register(functionId, CreateDummyInstanceFactory());

            CloudQueueMessage message = CreateMessage(functionId, "OriginalETag");

            // Act
            Task<bool> task = product.ExecuteAsync(message, CancellationToken.None);

            // Assert
            task.WaitUntilCompleted();
            mock.Verify();
            Assert.True(task.Result);
        }

        [Fact]
        public void ExecuteAsync_IfBlobIsUnchanged_CallsInnerExecutor()
        {
            // Arrange
            string functionId = "FunctionId";
            string matchingETag = "ETag";
            Guid expectedParentId = Guid.NewGuid();
            IBlobETagReader eTagReader = CreateStubETagReader(matchingETag);
            IBlobCausalityReader causalityReader = CreateStubCausalityReader(expectedParentId);
            Mock<IFunctionExecutor> mock = new Mock<IFunctionExecutor>(MockBehavior.Strict);
            mock.Setup(e => e.TryExecuteAsync(It.Is<IFunctionInstance>(f => f.ParentId == expectedParentId),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<IDelayedException>(null))
                .Verifiable();
            IFunctionExecutor innerExecutor = mock.Object;
            BlobQueueTriggerExecutor product = CreateProductUnderTest(eTagReader, causalityReader, innerExecutor);

            product.Register(functionId, CreateFakeInstanceFactory());

            CloudQueueMessage message = CreateMessage(functionId, matchingETag);

            // Act
            Task<bool> task = product.ExecuteAsync(message, CancellationToken.None);

            // Assert
            task.WaitUntilCompleted();
            mock.Verify();
        }

        [Fact]
        public void ExecuteAsync_IfInnerExecutorSucceeds_ReturnsTrue()
        {
            // Arrange
            string functionId = "FunctionId";
            string matchingETag = "ETag";
            IBlobETagReader eTagReader = CreateStubETagReader(matchingETag);
            IBlobCausalityReader causalityReader = CreateStubCausalityReader();
            IFunctionExecutor innerExecutor = CreateStubInnerExecutor(null);
            BlobQueueTriggerExecutor product = CreateProductUnderTest(eTagReader, causalityReader, innerExecutor);

            product.Register(functionId, CreateFakeInstanceFactory());

            CloudQueueMessage message = CreateMessage(functionId, matchingETag);

            // Act
            Task<bool> task = product.ExecuteAsync(message, CancellationToken.None);

            // Assert
            Assert.True(task.Result);
        }

        [Fact]
        public void ExecuteAsync_IfInnerExecutorFails_ReturnsFalse()
        {
            // Arrange
            string functionId = "FunctionId";
            string matchingETag = "ETag";
            IBlobETagReader eTagReader = CreateStubETagReader(matchingETag);
            IBlobCausalityReader causalityReader = CreateStubCausalityReader();
            IFunctionExecutor innerExecutor = CreateStubInnerExecutor(CreateDummyDelayedException());
            BlobQueueTriggerExecutor product = CreateProductUnderTest(eTagReader, causalityReader, innerExecutor);

            product.Register(functionId, CreateFakeInstanceFactory());

            CloudQueueMessage message = CreateMessage(functionId, matchingETag);

            // Act
            Task<bool> task = product.ExecuteAsync(message, CancellationToken.None);

            // Assert
            Assert.False(task.Result);
        }

        private static CloudBlobClient CreateClient()
        {
            CloudStorageAccount account = CloudStorageAccount.DevelopmentStorageAccount;
            return account.CreateCloudBlobClient();
        }

        private static IBlobWrittenWatcher CreateDummyBlobWrittenWatcher()
        {
            return new Mock<IBlobWrittenWatcher>(MockBehavior.Strict).Object;
        }

        private static IBlobCausalityReader CreateDummyCausalityReader()
        {
            return new Mock<IBlobCausalityReader>(MockBehavior.Strict).Object;
        }

        private static IDelayedException CreateDummyDelayedException()
        {
            return new Mock<IDelayedException>(MockBehavior.Strict).Object;
        }

        private static IBlobETagReader CreateDummyETagReader()
        {
            return new Mock<IBlobETagReader>(MockBehavior.Strict).Object;
        }

        private static IFunctionExecutor CreateDummyInnerExecutor()
        {
            return new Mock<IFunctionExecutor>(MockBehavior.Strict).Object;
        }

        private static ITriggeredFunctionInstanceFactory<ICloudBlob> CreateDummyInstanceFactory()
        {
            return new Mock<ITriggeredFunctionInstanceFactory<ICloudBlob>>(MockBehavior.Strict).Object;
        }

        private static ITriggeredFunctionInstanceFactory<ICloudBlob> CreateFakeInstanceFactory()
        {
            Mock<ITriggeredFunctionInstanceFactory<ICloudBlob>> mock =
                new Mock<ITriggeredFunctionInstanceFactory<ICloudBlob>>(MockBehavior.Strict);
            mock.Setup(f => f.Create(It.IsAny<ICloudBlob>(), It.IsAny<Guid?>()))
                .Returns<ICloudBlob, Guid?>((b, parentId) => CreateStubFunctionInstance(parentId));
            return mock.Object;
        }

        private static CloudQueueMessage CreateMessage(string functionId, string eTag)
        {
            BlobTriggerMessage triggerMessage = new BlobTriggerMessage
            {
                FunctionId = functionId,
                BlobType = BlobType.BlockBlob,
                ContainerName = "container",
                BlobName = "blob",
                ETag = eTag
            };
            return CreateMessage(triggerMessage);
        }

        private static CloudQueueMessage CreateMessage(BlobTriggerMessage triggerMessage)
        {
            return CreateMessage(JsonConvert.SerializeObject(triggerMessage));
        }

        private static CloudQueueMessage CreateMessage(string content)
        {
            return new CloudQueueMessage(content);
        }

        private static BlobQueueTriggerExecutor CreateProductUnderTest()
        {
            return CreateProductUnderTest(CreateDummyETagReader());
        }

        private static BlobQueueTriggerExecutor CreateProductUnderTest(IBlobETagReader eTagReader)
        {
            return CreateProductUnderTest(eTagReader, CreateDummyBlobWrittenWatcher());
        }

        private static BlobQueueTriggerExecutor CreateProductUnderTest(IBlobETagReader eTagReader,
            IBlobWrittenWatcher blobWrittenWatcher)
        {
            IBlobCausalityReader causalityReader = CreateDummyCausalityReader();
            IFunctionExecutor innerExecutor = CreateDummyInnerExecutor();
            return CreateProductUnderTest(eTagReader, causalityReader, innerExecutor, blobWrittenWatcher);
        }

        private static BlobQueueTriggerExecutor CreateProductUnderTest(IBlobETagReader eTagReader,
            IBlobCausalityReader causalityReader, IFunctionExecutor innerExecutor)
        {
            return CreateProductUnderTest(eTagReader, causalityReader, innerExecutor, CreateDummyBlobWrittenWatcher());
        }

        private static BlobQueueTriggerExecutor CreateProductUnderTest(IBlobETagReader eTagReader,
             IBlobCausalityReader causalityReader, IFunctionExecutor innerExecutor,
            IBlobWrittenWatcher blobWrittenWatcher)
        {
            CloudBlobClient client = CreateClient();
            return new BlobQueueTriggerExecutor(client, eTagReader, causalityReader, innerExecutor, blobWrittenWatcher);
        }

        private static IBlobCausalityReader CreateStubCausalityReader()
        {
            return CreateStubCausalityReader(null);
        }

        private static IBlobCausalityReader CreateStubCausalityReader(Guid? parentId)
        {
            Mock<IBlobCausalityReader> mock = new Mock<IBlobCausalityReader>(MockBehavior.Strict);
            mock.Setup(r => r.GetWriterAsync(It.IsAny<ICloudBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(parentId));
            return mock.Object;
        }

        private static IBlobETagReader CreateStubETagReader(string eTag)
        {
            Mock<IBlobETagReader> mock = new Mock<IBlobETagReader>(MockBehavior.Strict);
            mock.Setup(r => r.GetETagAsync(It.IsAny<ICloudBlob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<string>(eTag));
            return mock.Object;
        }

        private static IFunctionInstance CreateStubFunctionInstance(Guid? parentId)
        {
            Mock<IFunctionInstance> mock = new Mock<IFunctionInstance>(MockBehavior.Strict);
            mock.Setup(i => i.ParentId)
                .Returns(parentId);
            return mock.Object;
        }

        private static IFunctionExecutor CreateStubInnerExecutor(IDelayedException result)
        {
            Mock<IFunctionExecutor> mock = new Mock<IFunctionExecutor>(MockBehavior.Strict);
            mock.Setup(e => e.TryExecuteAsync(It.IsAny<IFunctionInstance>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(result));
            return mock.Object;
        }
    }
}
