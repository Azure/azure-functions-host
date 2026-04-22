// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
        }

        [Fact]
        public async Task LinkWorker_ValidRequest_Returns200Ok()
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

            var result = await _controller.LinkWorker("w_test1234", request);

            Assert.IsType<OkResult>(result);
        }

        [Fact]
        public async Task LinkWorker_NullRequest_Returns400()
        {
            var result = await _controller.LinkWorker("w_test1234", null);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task LinkWorker_MissingEndpoint_Returns400()
        {
            var request = new ExternalWorkerInfo { WorkerId = "w_test1234" };

            var result = await _controller.LinkWorker("w_test1234", request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task LinkWorker_InvalidEndpoint_Returns400()
        {
            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "not-a-valid-uri"
            };

            var result = await _controller.LinkWorker("w_test1234", request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task LinkWorker_MissingRouteWorkerId_Returns400(string routeWorkerId)
        {
            var request = new ExternalWorkerInfo
            {
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = await _controller.LinkWorker(routeWorkerId, request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("route", badRequest.Value.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task LinkWorker_BodyIdMismatchesRouteId_Returns400()
        {
            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_bodyid01",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = await _controller.LinkWorker("w_routeid1", request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("does not match", badRequest.Value.ToString());
        }

        [Fact]
        public async Task LinkWorker_NullBodyWorkerId_UsesRouteId()
        {
            var request = new ExternalWorkerInfo
            {
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            _mockConnectionManager
                .Setup(m => m.ConnectWorkerAsync("w_routeid1", It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = await _controller.LinkWorker("w_routeid1", request);

            Assert.IsType<OkResult>(result);
        }

        [Fact]
        public async Task LinkWorker_BeforeSpecialization_Returns400()
        {
            _mockWebHostEnvironment.Setup(e => e.InStandbyMode).Returns(true);

            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = await _controller.LinkWorker("w_test1234", request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("specialized", badRequest.Value.ToString());
        }

        [Fact]
        public async Task LinkWorker_DuplicateWorkerId_Returns409Conflict()
        {
            _mockConnectionManager
                .Setup(m => m.GetWorkerStatus("w_test1234"))
                .Returns(new WorkerConnectionInfo { WorkerId = "w_test1234", State = WorkerConnectionState.Connected });

            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = await _controller.LinkWorker("w_test1234", request);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.Contains("already linked", conflict.Value.ToString());
        }

        [Fact]
        public async Task LinkWorker_ConnectionFails_Returns503()
        {
            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            _mockConnectionManager
                .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("gRPC connection failed"));

            var result = await _controller.LinkWorker("w_test1234", request);

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
            Assert.Contains("connection failed", objectResult.Value.ToString());
        }

        [Fact]
        public async Task LinkWorker_RuntimeStopping_Returns409()
        {
            var request = new ExternalWorkerInfo
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            _mockConnectionManager
                .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Cannot connect new workers while the runtime is stopping."));

            var result = await _controller.LinkWorker("w_test1234", request);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.Contains("link rejected", conflict.Value.ToString());
        }
    }
}
