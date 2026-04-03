// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
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
        public void Assign_ValidRequest_Returns202Accepted()
        {
            var request = new WorkerAssignRequest
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            _mockConnectionManager
                .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = _controller.Assign(request);

            var accepted = Assert.IsType<AcceptedResult>(result);
            var info = Assert.IsType<WorkerConnectionInfo>(accepted.Value);
            Assert.Equal("w_test1234", info.WorkerId);
            Assert.Equal(WorkerConnectionState.Connecting, info.State);
        }

        [Fact]
        public void Assign_NullRequest_Returns400()
        {
            var result = _controller.Assign(null);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void Assign_MissingEndpoint_Returns400()
        {
            var request = new WorkerAssignRequest { WorkerId = "w_test1234" };

            var result = _controller.Assign(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void Assign_InvalidEndpoint_Returns400()
        {
            var request = new WorkerAssignRequest
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "not-a-valid-uri"
            };

            var result = _controller.Assign(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void Assign_NullWorkerId_GeneratesId()
        {
            var request = new WorkerAssignRequest
            {
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            string capturedWorkerId = null;
            _mockConnectionManager
                .Setup(m => m.ConnectWorkerAsync(It.IsAny<string>(), It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .Callback<string, Uri, CancellationToken>((id, _, _) => capturedWorkerId = id)
                .Returns(Task.CompletedTask);

            var result = _controller.Assign(request);

            var accepted = Assert.IsType<AcceptedResult>(result);
            var info = Assert.IsType<WorkerConnectionInfo>(accepted.Value);
            Assert.StartsWith("w_", info.WorkerId);
            Assert.Equal(WorkerConnectionState.Connecting, info.State);
        }

        [Fact]
        public void Assign_BeforeSpecialization_Returns400()
        {
            _mockWebHostEnvironment.Setup(e => e.InStandbyMode).Returns(true);

            var request = new WorkerAssignRequest
            {
                WorkerId = "w_test1234",
                GrpcEndpoint = "http://10.0.1.42:50051"
            };

            var result = _controller.Assign(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("specialized", badRequest.Value.ToString());
        }

        [Fact]
        public void GetAll_ReturnsWorkerList()
        {
            var workers = new List<WorkerConnectionInfo>
            {
                new() { WorkerId = "w_1", State = WorkerConnectionState.Connected },
                new() { WorkerId = "w_2", State = WorkerConnectionState.Connecting }
            };

            _mockConnectionManager
                .Setup(m => m.GetWorkerStatuses())
                .Returns(workers.AsReadOnly());

            var result = _controller.GetAll();

            var ok = Assert.IsType<OkObjectResult>(result);
            var returned = Assert.IsAssignableFrom<IReadOnlyList<WorkerConnectionInfo>>(ok.Value);
            Assert.Equal(2, returned.Count);
        }

        [Fact]
        public void Get_KnownWorker_ReturnsWorkerInfo()
        {
            var info = new WorkerConnectionInfo { WorkerId = "w_1", State = WorkerConnectionState.Connected };

            _mockConnectionManager
                .Setup(m => m.GetWorkerStatus("w_1"))
                .Returns(info);

            var result = _controller.Get("w_1");

            var ok = Assert.IsType<OkObjectResult>(result);
            var returned = Assert.IsType<WorkerConnectionInfo>(ok.Value);
            Assert.Equal("w_1", returned.WorkerId);
            Assert.Equal(WorkerConnectionState.Connected, returned.State);
        }

        [Fact]
        public void Get_UnknownWorker_Returns404()
        {
            _mockConnectionManager
                .Setup(m => m.GetWorkerStatus("w_unknown"))
                .Returns((WorkerConnectionInfo)null);

            var result = _controller.Get("w_unknown");

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Delete_KnownWorker_Returns200()
        {
            _mockConnectionManager
                .Setup(m => m.GetWorkerStatus("w_1"))
                .Returns(new WorkerConnectionInfo { WorkerId = "w_1", State = WorkerConnectionState.Connected });

            _mockConnectionManager
                .Setup(m => m.DisconnectWorkerAsync("w_1", It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    // After disconnect, update the mock to return Disconnected state
                    _mockConnectionManager
                        .Setup(m => m.GetWorkerStatus("w_1"))
                        .Returns(new WorkerConnectionInfo { WorkerId = "w_1", State = WorkerConnectionState.Disconnected });
                })
                .Returns(Task.CompletedTask);

            var result = await _controller.Delete("w_1", CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var info = Assert.IsType<WorkerConnectionInfo>(ok.Value);
            Assert.Equal(WorkerConnectionState.Disconnected, info.State);
        }

        [Fact]
        public async Task Delete_UnknownWorker_Returns404()
        {
            _mockConnectionManager
                .Setup(m => m.GetWorkerStatus("w_unknown"))
                .Returns((WorkerConnectionInfo)null);

            var result = await _controller.Delete("w_unknown", CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
        }
    }
}
