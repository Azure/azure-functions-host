// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class PlaceholderSpecializationMiddlewareTests
    {
        [Fact]
        public async Task Invoke_FlexConsumption_AddsHeader_When_Specialized()
        {
            // Arrange
            var webHostEnvironment = new Mock<IScriptWebHostEnvironment>();
            var standbyManager = new Mock<IStandbyManager>();
            var environment = new Mock<IEnvironment>();
            var httpContext = new DefaultHttpContext();
            
            // Setup conditions for specialization
            webHostEnvironment.Setup(e => e.InStandbyMode).Returns(false);
            environment.Setup(e => e.IsContainerReady()).Returns(true);
            environment.Setup(e => e.IsFlexConsumptionSku()).Returns(true);
            
            standbyManager.Setup(s => s.SpecializeHostAsync()).Returns(Task.CompletedTask);

            var nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new PlaceholderSpecializationMiddleware(next, webHostEnvironment.Object, standbyManager.Object, environment.Object);

            // Act
            await middleware.Invoke(httpContext);

            // Assert
            Assert.True(nextCalled);
            Assert.Equal("1", httpContext.Request.Headers[ScriptConstants.AntaresColdStartHeaderName]);
            standbyManager.Verify(s => s.SpecializeHostAsync(), Times.Once);
        }

        [Fact]
        public async Task Invoke_NonFlexConsumption_DoesNotAddHeader_When_Specialized()
        {
            // Arrange
            var webHostEnvironment = new Mock<IScriptWebHostEnvironment>();
            var standbyManager = new Mock<IStandbyManager>();
            var environment = new Mock<IEnvironment>();
            var httpContext = new DefaultHttpContext();
            
            // Setup conditions for specialization but NOT Flex
            webHostEnvironment.Setup(e => e.InStandbyMode).Returns(false);
            environment.Setup(e => e.IsContainerReady()).Returns(true);
            environment.Setup(e => e.IsFlexConsumptionSku()).Returns(false);
            
            standbyManager.Setup(s => s.SpecializeHostAsync()).Returns(Task.CompletedTask);

            var nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new PlaceholderSpecializationMiddleware(next, webHostEnvironment.Object, standbyManager.Object, environment.Object);

            // Act
            await middleware.Invoke(httpContext);

            // Assert
            Assert.True(nextCalled);
            Assert.False(httpContext.Request.Headers.ContainsKey(ScriptConstants.AntaresColdStartHeaderName));
            standbyManager.Verify(s => s.SpecializeHostAsync(), Times.Once);
        }

        [Fact]
        public async Task Invoke_NoSpecialization_DoesNotAddHeader()
        {
            // Arrange
            var webHostEnvironment = new Mock<IScriptWebHostEnvironment>();
            var standbyManager = new Mock<IStandbyManager>();
            var environment = new Mock<IEnvironment>();
            var httpContext = new DefaultHttpContext();
            
            // Setup conditions for NO specialization
            webHostEnvironment.Setup(e => e.InStandbyMode).Returns(true);
            environment.Setup(e => e.IsContainerReady()).Returns(true);
            
            var nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new PlaceholderSpecializationMiddleware(next, webHostEnvironment.Object, standbyManager.Object, environment.Object);

            // Act
            await middleware.Invoke(httpContext);

            // Assert
            Assert.True(nextCalled);
            Assert.False(httpContext.Request.Headers.ContainsKey(ScriptConstants.AntaresColdStartHeaderName));
            standbyManager.Verify(s => s.SpecializeHostAsync(), Times.Never);
        }
    }
}