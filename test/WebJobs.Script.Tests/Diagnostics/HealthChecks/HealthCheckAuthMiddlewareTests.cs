// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.HealthChecks;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    public class HealthCheckAuthMiddlewareTests
    {
        [Fact]
        public void Constructor_NullNext_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new HealthCheckAuthMiddleware(
                null, Mock.Of<IPolicyEvaluator>(), Mock.Of<IAuthorizationPolicyProvider>()))
                .Should().Throw<ArgumentNullException>().WithParameterName("next");
        }

        [Fact]
        public void Constructor_NullPolicy_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new HealthCheckAuthMiddleware(
                Mock.Of<RequestDelegate>(), null, Mock.Of<IAuthorizationPolicyProvider>()))
                .Should().Throw<ArgumentNullException>().WithParameterName("policy");
        }

        [Fact]
        public void Constructor_NullProvider_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new HealthCheckAuthMiddleware(
                Mock.Of<RequestDelegate>(), Mock.Of<IPolicyEvaluator>(), null))
                .Should().Throw<ArgumentNullException>().WithParameterName("provider");
        }

        [Fact]
        public async Task InvokeAsync_NullContext_Throws()
        {
            HealthCheckAuthMiddleware middleware = new(
                Mock.Of<RequestDelegate>(),
                Mock.Of<IPolicyEvaluator>(),
                Mock.Of<IAuthorizationPolicyProvider>());

            await TestHelpers.Act(() => middleware.InvokeAsync(null))
                .Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("?other=true")]
        [InlineData("?expand=false")]
        [InlineData("?expand=notabool")]
        public async Task InvokeAsync_NotExpanded_SkipsAuthAndCallsNext(string query)
        {
            Mock<RequestDelegate> next = new();
            Mock<IPolicyEvaluator> policy = new(MockBehavior.Strict);
            Mock<IAuthorizationPolicyProvider> provider = new(MockBehavior.Strict);
            HttpContext context = CreateContext(query);
            HealthCheckAuthMiddleware middleware = new(next.Object, policy.Object, provider.Object);

            await middleware.InvokeAsync(context);

            next.Verify(m => m(context), Times.Once);
            next.VerifyNoOtherCalls();
            policy.VerifyNoOtherCalls();
            provider.VerifyNoOtherCalls();
            context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        [Fact]
        public async Task InvokeAsync_Expanded_AuthenticationFails_Returns401()
        {
            Mock<RequestDelegate> next = new();
            AuthorizationPolicy authPolicy = new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true).Build();
            Mock<IPolicyEvaluator> policy = new(MockBehavior.Strict);
            policy.Setup(p => p.AuthenticateAsync(authPolicy, It.IsAny<HttpContext>()))
                .ReturnsAsync(AuthenticateResult.Fail("nope"));
            Mock<IAuthorizationPolicyProvider> provider = new(MockBehavior.Strict);
            provider.Setup(p => p.GetPolicyAsync(PolicyNames.AdminAuthLevel)).ReturnsAsync(authPolicy);

            HttpContext context = CreateContext("?expand=true");
            HealthCheckAuthMiddleware middleware = new(next.Object, policy.Object, provider.Object);

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
            next.Verify(m => m(context), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_Expanded_AuthorizationFails_Returns403()
        {
            Mock<RequestDelegate> next = new();
            AuthorizationPolicy authPolicy = new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true).Build();
            AuthenticateResult authSuccess = AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("test")), "test"));
            Mock<IPolicyEvaluator> policy = new(MockBehavior.Strict);
            policy.Setup(p => p.AuthenticateAsync(authPolicy, It.IsAny<HttpContext>()))
                .ReturnsAsync(authSuccess);
            policy.Setup(p => p.AuthorizeAsync(authPolicy, authSuccess, It.IsAny<HttpContext>(), null))
                .ReturnsAsync(PolicyAuthorizationResult.Forbid());
            Mock<IAuthorizationPolicyProvider> provider = new(MockBehavior.Strict);
            provider.Setup(p => p.GetPolicyAsync(PolicyNames.AdminAuthLevel)).ReturnsAsync(authPolicy);

            HttpContext context = CreateContext("?expand=true");
            HealthCheckAuthMiddleware middleware = new(next.Object, policy.Object, provider.Object);

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            next.Verify(m => m(context), Times.Never);
        }

        [Fact]
        public async Task InvokeAsync_Expanded_AuthorizationSucceeds_CallsNext()
        {
            Mock<RequestDelegate> next = new();
            AuthorizationPolicy authPolicy = new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => true).Build();
            AuthenticateResult authSuccess = AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("test")), "test"));
            Mock<IPolicyEvaluator> policy = new(MockBehavior.Strict);
            policy.Setup(p => p.AuthenticateAsync(authPolicy, It.IsAny<HttpContext>()))
                .ReturnsAsync(authSuccess);
            policy.Setup(p => p.AuthorizeAsync(authPolicy, authSuccess, It.IsAny<HttpContext>(), null))
                .ReturnsAsync(PolicyAuthorizationResult.Success());
            Mock<IAuthorizationPolicyProvider> provider = new(MockBehavior.Strict);
            provider.Setup(p => p.GetPolicyAsync(PolicyNames.AdminAuthLevel)).ReturnsAsync(authPolicy);

            HttpContext context = CreateContext("?expand=true");
            HealthCheckAuthMiddleware middleware = new(next.Object, policy.Object, provider.Object);

            await middleware.InvokeAsync(context);

            next.Verify(m => m(context), Times.Once);
            context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        private static DefaultHttpContext CreateContext(string query)
        {
            DefaultHttpContext context = new();
            context.Request.QueryString = new QueryString(query);

            return context;
        }
    }
}
