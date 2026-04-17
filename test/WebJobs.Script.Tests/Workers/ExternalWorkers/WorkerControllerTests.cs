// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers
{
    public class WorkerControllerTests
    {
        private readonly Mock<IWorkerConnectionManager> _mockConnectionManager;
        private readonly Mock<IScriptWebHostEnvironment> _mockWebHostEnvironment;
        private readonly WorkerController _controller;

        public WorkerControllerTests()
        {
            _mockConnectionManager = new Mock<IWorkerConnectionManager>();
            _mockWebHostEnvironment = new Mock<IScriptWebHostEnvironment>();
            _mockWebHostEnvironment.Setup(e => e.InStandbyMode).Returns(false);
            _controller = new WorkerController(_mockConnectionManager.Object, _mockWebHostEnvironment.Object, NullLoggerFactory.Instance);
        }

        [Fact]
        public void LinkWorker_ValidRequest_Returns202Accepted()
        {
            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_test1234",
                PodName = "worker-pod-abc123",
                GrpcEndpoint = "http://10.0.1.42:50051",
                PodKey = "test-key"
            };

            _mockConnectionManager
                .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = _controller.LinkWorker("w_test1234", request);

            var accepted = Assert.IsType<AcceptedResult>(result);
            var info = Assert.IsType<WorkerConnectionInfo>(accepted.Value);
            Assert.Equal("w_test1234", info.WorkerId);
            Assert.Equal(WorkerConnectionState.Connecting, info.State);
        }

        [Fact]
        public void LinkWorker_NullRequest_Returns400()
        {
            var result = _controller.LinkWorker("w_test1234", null);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void LinkWorker_MissingEndpoint_Returns400()
        {
            var request = new ExternalWorkerInfo { WorkerId = "w_test1234" };

            var result = _controller.LinkWorker("w_test1234", request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void LinkWorker_InvalidEndpoint_Returns400()
        {
            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "not-a-valid-uri"
            };

            var result = _controller.LinkWorker("w_test1234", request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void LinkWorker_MissingRouteWorkerId_Returns400(string routeWorkerId)
        {
            var request = new ExternalWorkerInfo
            {
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = _controller.LinkWorker(routeWorkerId, request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("route", badRequest.Value.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LinkWorker_BodyIdMismatchesRouteId_Returns400()
        {
            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_bodyid01",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = _controller.LinkWorker("w_routeid1", request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("does not match", badRequest.Value.ToString());
        }

        [Fact]
        public void LinkWorker_NullBodyWorkerId_UsesRouteId()
        {
            var request = new ExternalWorkerInfo
            {
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            _mockConnectionManager
                .Setup(m => m.ConnectWorkerAsync("w_routeid1", It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = _controller.LinkWorker("w_routeid1", request);

            var accepted = Assert.IsType<AcceptedResult>(result);
            var info = Assert.IsType<WorkerConnectionInfo>(accepted.Value);
            Assert.Equal("w_routeid1", info.WorkerId);
            Assert.Equal(WorkerConnectionState.Connecting, info.State);
        }

        [Fact]
        public void LinkWorker_BeforeSpecialization_Returns400()
        {
            _mockWebHostEnvironment.Setup(e => e.InStandbyMode).Returns(true);

            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = _controller.LinkWorker("w_test1234", request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("specialized", badRequest.Value.ToString());
        }

        [Fact]
        public void LinkWorker_DuplicateWorkerId_Returns409Conflict()
        {
            _mockConnectionManager
                .Setup(m => m.GetWorkerStatus("w_test1234"))
                .Returns(new WorkerConnectionInfo { WorkerId = "w_test1234", State = WorkerConnectionState.Connected });

            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = _controller.LinkWorker("w_test1234", request);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.Contains("already linked", conflict.Value.ToString());
        }
    }
}
