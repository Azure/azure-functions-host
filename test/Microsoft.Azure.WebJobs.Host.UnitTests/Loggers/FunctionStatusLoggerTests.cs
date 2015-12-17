// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class FunctionStatusLoggerTests
    {
        private readonly Mock<IStorageBlobContainer> _containerMock = new Mock<IStorageBlobContainer>(MockBehavior.Strict);
        private readonly string _hostId = "TestHostId";
        private readonly FunctionStatusLogger _logger;

        public FunctionStatusLoggerTests()
        {
            Mock<IStorageBlobClient> blobClientMock = new Mock<IStorageBlobClient>(MockBehavior.Strict);
            blobClientMock.Setup(p => p.GetContainerReference(HostContainerNames.Hosts)).Returns(_containerMock.Object);
            _logger = new FunctionStatusLogger(new FixedHostIdProvider(_hostId), blobClientMock.Object);
        }

        [Fact]
        public void CreateFunctionStatusMessage_FunctionStart_CreatesExpectedMessage()
        {
            FunctionStartedMessage startMessage = new FunctionStartedMessage
            {
                Function = new FunctionDescriptor
                {
                    Id = "TestHost.TestFunction"
                },
                FunctionInstanceId = Guid.NewGuid(),
                StartTime = DateTime.Now,
                OutputBlob = new LocalBlobDescriptor(),
                ParameterLogBlob = new LocalBlobDescriptor()
            };

            FunctionStatusMessage statusMessage = FunctionStatusLogger.CreateFunctionStatusMessage(startMessage);

            Assert.Equal(startMessage.Function.Id, statusMessage.FunctionId);
            Assert.Equal(startMessage.FunctionInstanceId, statusMessage.FunctionInstanceId);
            Assert.Equal("Started", statusMessage.Status);
            Assert.Equal(startMessage.StartTime, statusMessage.StartTime);
            Assert.Same(startMessage.OutputBlob, statusMessage.OutputBlob);
            Assert.Same(startMessage.ParameterLogBlob, statusMessage.ParameterLogBlob);
        }

        [Fact]
        public void CreateFunctionStatusMessage_FunctionComplete_CreatesExpectedMessage()
        {
            FunctionCompletedMessage completedMessage = new FunctionCompletedMessage
            {
                Function = new FunctionDescriptor
                {
                    Id = "TestHost.TestFunction"
                },
                FunctionInstanceId = Guid.NewGuid(),
                StartTime = DateTime.Now,
                EndTime = DateTime.Now,
                OutputBlob = new LocalBlobDescriptor(),
                ParameterLogBlob = new LocalBlobDescriptor()
            };

            FunctionStatusMessage statusMessage = FunctionStatusLogger.CreateFunctionStatusMessage(completedMessage);

            Assert.Equal(completedMessage.Function.Id, statusMessage.FunctionId);
            Assert.Equal(completedMessage.FunctionInstanceId, statusMessage.FunctionInstanceId);
            Assert.Equal("Completed", statusMessage.Status);
            Assert.Equal(completedMessage.StartTime, statusMessage.StartTime);
            Assert.Equal(completedMessage.EndTime, statusMessage.EndTime);
            Assert.Same(completedMessage.Failure, statusMessage.Failure);
            Assert.Same(completedMessage.OutputBlob, statusMessage.OutputBlob);
            Assert.Same(completedMessage.ParameterLogBlob, statusMessage.ParameterLogBlob);

            completedMessage.Failure = new FunctionFailure();
            statusMessage = FunctionStatusLogger.CreateFunctionStatusMessage(completedMessage);
            Assert.Same(completedMessage.Failure, statusMessage.Failure);
        }

        [Fact]
        public async Task LogFunctionStartedAsync_Portal_WritesExpectedBlob()
        {
            FunctionStartedMessage startMessage = new FunctionStartedMessage
            {
                Function = new FunctionDescriptor
                {
                    Id = "TestHost.TestFunction"
                },
                FunctionInstanceId = Guid.NewGuid(),
                StartTime = DateTime.Now,
                OutputBlob = new LocalBlobDescriptor(),
                ParameterLogBlob = new LocalBlobDescriptor(),
                Reason = ExecutionReason.Portal
            };
            FunctionStatusMessage statusMessage = FunctionStatusLogger.CreateFunctionStatusMessage(startMessage);

            CancellationToken cancellationToken = new CancellationToken();
            string expectedContent = JsonConvert.SerializeObject(statusMessage, JsonSerialization.Settings);
            string blobName = string.Format("invocations/{0}/{1}/{2}", _hostId, startMessage.Function.Id, startMessage.FunctionInstanceId);
            Mock<IStorageBlockBlob> blobMock = new Mock<IStorageBlockBlob>(MockBehavior.Strict);
            blobMock.Setup(p => p.UploadTextAsync(expectedContent, null, null, null, null, cancellationToken)).Returns(Task.FromResult(0));
            _containerMock.Setup(p => p.GetBlockBlobReference(blobName)).Returns(blobMock.Object);

            string id = await _logger.LogFunctionStartedAsync(startMessage, cancellationToken);

            Assert.Null(id);
            _containerMock.VerifyAll();
            blobMock.VerifyAll();
        }

        [Fact]
        public async Task LogFunctionStartedAsync_NonPortal_DoesNotWriteBlob()
        {
            FunctionStartedMessage startMessage = new FunctionStartedMessage
            {
                Reason = ExecutionReason.Dashboard
            };

            // Since we haven't set up the mocks, this would throw if it didn't noop
            string id = await _logger.LogFunctionStartedAsync(startMessage, CancellationToken.None);

            Assert.Null(id);
        }

        [Fact]
        public async Task LogFunctionCompletedAsync_Portal_WritesExpectedBlob()
        {
            FunctionCompletedMessage completedMessage = new FunctionCompletedMessage
            {
                Function = new FunctionDescriptor
                {
                    Id = "TestHost.TestFunction"
                },
                FunctionInstanceId = Guid.NewGuid(),
                StartTime = DateTime.Now,
                EndTime = DateTime.Now,
                OutputBlob = new LocalBlobDescriptor(),
                ParameterLogBlob = new LocalBlobDescriptor(),
                Reason = ExecutionReason.Portal
            };
            FunctionStatusMessage statusMessage = FunctionStatusLogger.CreateFunctionStatusMessage(completedMessage);

            CancellationToken cancellationToken = new CancellationToken();
            string expectedContent = JsonConvert.SerializeObject(statusMessage, JsonSerialization.Settings);
            string blobName = string.Format("invocations/{0}/{1}/{2}", _hostId, completedMessage.Function.Id, completedMessage.FunctionInstanceId);
            Mock<IStorageBlockBlob> blobMock = new Mock<IStorageBlockBlob>(MockBehavior.Strict);
            blobMock.Setup(p => p.UploadTextAsync(expectedContent, null, null, null, null, cancellationToken)).Returns(Task.FromResult(0));
            _containerMock.Setup(p => p.GetBlockBlobReference(blobName)).Returns(blobMock.Object);

            await _logger.LogFunctionCompletedAsync(completedMessage, cancellationToken);

            _containerMock.VerifyAll();
            blobMock.VerifyAll();
        }

        [Fact]
        public async Task LogFunctionCompletedAsync_NonPortal_DoesNotWriteBlob()
        {
            FunctionCompletedMessage completedMessage = new FunctionCompletedMessage
            {
                Reason = ExecutionReason.Dashboard
            };

            // Since we haven't set up the mocks, this would throw if it didn't noop
            await _logger.LogFunctionCompletedAsync(completedMessage, CancellationToken.None);
        }

        [Fact]
        public async Task DeleteLogFunctionStartedAsync_Noops()
        {
            await _logger.DeleteLogFunctionStartedAsync("1234", CancellationToken.None);
        }
    }
}
