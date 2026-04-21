// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.ContainerInstanceTests)]
    public class RequestSlotsControllerTests
    {
        private static RequestSlotsController CreateController(IRuntimeStateManager runtimeStateManager)
        {
            var loggerFactory = new LoggerFactory();

            var controller = new RequestSlotsController(loggerFactory);

            var services = new Mock<IServiceProvider>();
            services.Setup(s => s.GetService(typeof(IRuntimeStateManager))).Returns(runtimeStateManager);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services.Object }
            };

            return controller;
        }

        [Fact]
        public void AcquireLeases_WhenExternalWorkersDisabled_Returns503()
        {
            var controller = CreateController(runtimeStateManager: null);

            var result = controller.AcquireLeases(new RequestSlotsLeaseRequest { Count = 5 });

            var statusResult = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusResult.StatusCode);
        }

        [Fact]
        public void AcquireLeases_NullBody_ReturnsBadRequest()
        {
            var mock = new Mock<IRuntimeStateManager>(MockBehavior.Strict);
            var controller = CreateController(mock.Object);

            var result = controller.AcquireLeases(null);

            Assert.IsType<BadRequestObjectResult>(result);
            mock.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void AcquireLeases_NonPositiveCount_ReturnsBadRequest(int count)
        {
            var mock = new Mock<IRuntimeStateManager>(MockBehavior.Strict);
            var controller = CreateController(mock.Object);

            var result = controller.AcquireLeases(new RequestSlotsLeaseRequest { Count = count });

            Assert.IsType<BadRequestObjectResult>(result);
            mock.VerifyNoOtherCalls();
        }

        [Fact]
        public void AcquireLeases_ReturnsGrantedCount()
        {
            var mock = new Mock<IRuntimeStateManager>();
            mock.Setup(m => m.AcquireSlots(5)).Returns(3);
            var controller = CreateController(mock.Object);

            var result = controller.AcquireLeases(new RequestSlotsLeaseRequest { Count = 5 });

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<RequestSlotsLeaseResponse>(ok.Value);
            Assert.Equal(3, payload.AcquiredSlotCount);
            mock.Verify(m => m.AcquireSlots(5), Times.Once);
        }

        [Fact]
        public void AcquireLeases_FullGrant_ReturnsRequestedCount()
        {
            var mock = new Mock<IRuntimeStateManager>();
            mock.Setup(m => m.AcquireSlots(4)).Returns(4);
            var controller = CreateController(mock.Object);

            var result = controller.AcquireLeases(new RequestSlotsLeaseRequest { Count = 4 });

            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = Assert.IsType<RequestSlotsLeaseResponse>(ok.Value);
            Assert.Equal(4, payload.AcquiredSlotCount);
        }

        [Fact]
        public void ReleaseLeases_WhenExternalWorkersDisabled_Returns503()
        {
            var controller = CreateController(runtimeStateManager: null);

            var result = controller.ReleaseLeases(new RequestSlotsLeaseRequest { Count = 2 });

            var statusResult = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusResult.StatusCode);
        }

        [Fact]
        public void ReleaseLeases_NullBody_ReturnsBadRequest()
        {
            var mock = new Mock<IRuntimeStateManager>(MockBehavior.Strict);
            var controller = CreateController(mock.Object);

            var result = controller.ReleaseLeases(null);

            Assert.IsType<BadRequestObjectResult>(result);
            mock.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void ReleaseLeases_NonPositiveCount_ReturnsBadRequest(int count)
        {
            var mock = new Mock<IRuntimeStateManager>(MockBehavior.Strict);
            var controller = CreateController(mock.Object);

            var result = controller.ReleaseLeases(new RequestSlotsLeaseRequest { Count = count });

            Assert.IsType<BadRequestObjectResult>(result);
            mock.VerifyNoOtherCalls();
        }

        [Fact]
        public void ReleaseLeases_DelegatesToManagerAndReturnsOk()
        {
            var mock = new Mock<IRuntimeStateManager>();
            var controller = CreateController(mock.Object);

            var result = controller.ReleaseLeases(new RequestSlotsLeaseRequest { Count = 7 });

            Assert.IsType<OkResult>(result);
            mock.Verify(m => m.ReleaseSlots(7), Times.Once);
        }
    }
}
