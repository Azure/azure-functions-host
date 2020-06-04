// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Binding;
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
            await TestAuthorizationAsync(claimAuthLevel: AuthorizationLevel.Function, requiredFunctionLevel: AuthorizationLevel.Function);
        }

        [Fact]
        public async Task Authorization_WithAdminAuthLevel_Succeeds()
        {
            await TestAuthorizationAsync(claimAuthLevel: AuthorizationLevel.Admin, requiredFunctionLevel: AuthorizationLevel.Function);
        }

        [Fact]
        public async Task Authorization_WithoutHttpTrigger_Fails()
        {
            await TestAuthorizationAsync(
                claimAuthLevel: AuthorizationLevel.Function,
                requiredFunctionLevel: AuthorizationLevel.Function,
                descriptor: new Mock<FunctionDescriptor>(),
                expectSuccess: false);
        }

        private async Task TestAuthorizationAsync(
            AuthorizationLevel claimAuthLevel,
            AuthorizationLevel requiredFunctionLevel,
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
