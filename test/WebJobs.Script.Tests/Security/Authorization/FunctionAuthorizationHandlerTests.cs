// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security.Authorization
{
    public class FunctionAuthorizationHandlerTests
    {
        [Fact]
        public async Task Authorization_WithExpectedAuthLevelMatch_Succeeds()
        {
            await TestAuthorizationAsync(claimAuthLevel: AuthorizationLevel.Function, requiredFunctionLevel: AuthorizationLevel.Function, invokeClaimValue: "true");
        }

        [Fact]
        public async Task Authorization_WithAdminAuthLevel_Succeeds()
        {
            await TestAuthorizationAsync(claimAuthLevel: AuthorizationLevel.Admin, requiredFunctionLevel: AuthorizationLevel.Function, invokeClaimValue: "true");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("invalid")]
        [InlineData("False")]
        public async Task Authorization_WithoutInvokeClaim_Fails(string invokeClaimValue)
        {
            await TestAuthorizationAsync(claimAuthLevel: AuthorizationLevel.Function, requiredFunctionLevel: AuthorizationLevel.Function, invokeClaimValue: invokeClaimValue, expectSuccess: false);
        }

        [Fact]
        public async Task Authorization_Anonymous_WithoutInvokeClaim_Succeeds()
        {
            await TestAuthorizationAsync(claimAuthLevel: AuthorizationLevel.Anonymous, requiredFunctionLevel: AuthorizationLevel.Anonymous, invokeClaimValue: null);
        }

        [Fact]
        public async Task Authorization_WithoutHttpTrigger_Fails()
        {
            await TestAuthorizationAsync(
                claimAuthLevel: AuthorizationLevel.Function,
                requiredFunctionLevel: AuthorizationLevel.Function,
                invokeClaimValue: "true",
                descriptor: new Mock<FunctionDescriptor>(),
                expectSuccess: false);
        }

        private async Task TestAuthorizationAsync(
            AuthorizationLevel claimAuthLevel,
            AuthorizationLevel requiredFunctionLevel,
            string invokeClaimValue,
            bool expectSuccess = true,
            Mock<FunctionDescriptor> descriptor = null)
        {
            descriptor = descriptor ?? CreateDefaultDescriptor(requiredFunctionLevel);

            var authHandler = new FunctionAuthorizationHandler();

            var requirements = new IAuthorizationRequirement[] { new FunctionAuthorizationRequirement() };
            var claims = new List<Claim>
            {
                new Claim(SecurityConstants.AuthLevelClaimType, claimAuthLevel.ToString())
            };
            if (invokeClaimValue != null)
            {
                claims.Add(new Claim(SecurityConstants.InvokeClaimType, invokeClaimValue));
            }

            var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

            var authHandlerContext = new AuthorizationHandlerContext(requirements, user, descriptor.Object);
            await authHandler.HandleAsync(authHandlerContext);

            Assert.Equal(expectSuccess, authHandlerContext.HasSucceeded);
        }

        private Mock<FunctionDescriptor> CreateDefaultDescriptor(AuthorizationLevel triggerLevel)
        {
            var descriptor = new Mock<FunctionDescriptor>();
            descriptor.SetupGet(d => d.HttpTriggerAttribute)
                .Returns(new HttpTriggerAttribute(triggerLevel));

            return descriptor;
        }
    }
}
