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
        public void Link_ValidRequest_Returns202Accepted()
        {
            var request = new WorkerLinkRequest
            {
                WorkerId = "w_test1234",
                PodName = "worker-pod-abc123",
                GrpcEndpoint = "http://10.0.1.42:50051",
                PodKey = "test-key"
            };

            _mockConnectionManager
                .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = _controller.Link(request);

            var accepted = Assert.IsType<AcceptedResult>(result);
            var info = Assert.IsType<WorkerConnectionInfo>(accepted.Value);
            Assert.Equal("w_test1234", info.WorkerId);
            Assert.Equal(WorkerConnectionState.Connecting, info.State);
        }

        [Fact]
        public void Link_NullRequest_Returns400()
        {
            var result = _controller.Link(null);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void Link_MissingEndpoint_Returns400()
        {
            var request = new WorkerLinkRequest { WorkerId = "w_test1234" };

            var result = _controller.Link(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void Link_InvalidEndpoint_Returns400()
        {
            var request = new WorkerLinkRequest
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "not-a-valid-uri"
            };

            var result = _controller.Link(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void Link_NullWorkerId_GeneratesId()
        {
            var request = new WorkerLinkRequest
            {
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            _mockConnectionManager
                .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = _controller.Link(request);

            var accepted = Assert.IsType<AcceptedResult>(result);
            var info = Assert.IsType<WorkerConnectionInfo>(accepted.Value);
            Assert.StartsWith("w_", info.WorkerId);
            Assert.Equal(WorkerConnectionState.Connecting, info.State);
        }

        [Fact]
        public void Link_BeforeSpecialization_Returns400()
        {
            _mockWebHostEnvironment.Setup(e => e.InStandbyMode).Returns(true);

            var request = new WorkerLinkRequest
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = _controller.Link(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("specialized", badRequest.Value.ToString());
        }

        [Fact]
        public void Link_DuplicateWorkerId_Returns409Conflict()
        {
            _mockConnectionManager
                .Setup(m => m.GetWorkerStatus("w_test1234"))
                .Returns(new WorkerConnectionInfo { WorkerId = "w_test1234", State = WorkerConnectionState.Connected });

            var request = new WorkerLinkRequest
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = _controller.Link(request);

            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.Contains("already linked", conflict.Value.ToString());
        }
    }
}
