// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.HealthChecks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    public class HealthCheckWaitMiddlewareTests
    {
        [Fact]
        public void Constructor_NullNext_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new HealthCheckWaitMiddleware(null, Mock.Of<IScriptHostManager>()))
                .Should().Throw<ArgumentNullException>().WithParameterName("next");
        }

        [Fact]
        public void Constructor_NullManager_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new HealthCheckWaitMiddleware(Mock.Of<RequestDelegate>(), null))
                .Should().Throw<ArgumentNullException>().WithParameterName("manager");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("?not_a_known_query_value=true")]
        public async Task InvokeAsync_NoWaitQuery_Continues(string query)
        {
            // arrange
            Mock<RequestDelegate> next = new();
            Mock<IScriptHostManager> manager = new(MockBehavior.Strict);
            HttpContext context = CreateContext(query, null);
            HealthCheckWaitMiddleware middleware = new(next.Object, manager.Object);

            // act
            await middleware.InvokeAsync(context);

            // assert
            next.Verify(m => m(context), Times.Once);
            next.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData("10s")]
        [InlineData("true")]
        [InlineData("-10")]
        public async Task InvokeAsync_InvalidWaitQuery_BadRequest(string wait)
        {
            // arrange
            using MemoryStream stream = new();
            Mock<RequestDelegate> next = new();
            Mock<IScriptHostManager> manager = new(MockBehavior.Strict);
            HttpContext context = CreateContext($"?wait={wait}", stream);
            HealthCheckWaitMiddleware middleware = new(next.Object, manager.Object);

            // act
            await middleware.InvokeAsync(context);
            stream.Position = 0;

            // assert
            next.Verify(m => m(context), Times.Never);
            next.VerifyNoOtherCalls();

            context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            ErrorResponse error = await JsonSerializer.DeserializeAsync<ErrorResponse>(stream);
            error.Code.Should().Be("BadArgument");
        }

        [Fact]
        public async Task InvokeAsync_ValidWaitQuery_CallsDelayAndNext()
        {
            // arrange
            // each failed loop is 3 calls, plus 1 for the final call.
            int neededCalls = (Random.Shared.Next(1, 10) * 3) + 1;
            int calls = 0;
            TaskCompletionSource tcs = new();
            Mock<RequestDelegate> next = new();
            Mock<IScriptHostManager> manager = new(MockBehavior.Strict);
            manager.Setup(m => m.State).Returns(() =>
            {
                // The check calls this 2 times per loop.
                return calls++ < neededCalls ? ScriptHostState.Starting : ScriptHostState.Running;
            });

            HttpContext context = CreateContext($"?wait=5", null);
            var middleware = new HealthCheckWaitMiddleware(next.Object, manager.Object);

            // act
            await middleware.InvokeAsync(context);

            // assert
            manager.Verify(m => m.State, Times.Exactly(neededCalls + 3));
            next.Verify(m => m(context), Times.Once);
            next.VerifyNoOtherCalls();
        }

        private static DefaultHttpContext CreateContext(string query, Stream body)
        {
            DefaultHttpContext context = new();
            context.Request.QueryString = new(query);
            if (body is not null)
            {
                context.Response.Body = body;
            }

            return context;
        }
    }
}