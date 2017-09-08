// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class AuthUtilityTests
    {
        [Theory]
        [InlineData(AuthorizationLevel.Admin, AuthorizationLevel.Function, true)]
        [InlineData(AuthorizationLevel.Function, AuthorizationLevel.Function, true)]
        [InlineData(AuthorizationLevel.System, AuthorizationLevel.Function, false)]
        [InlineData(AuthorizationLevel.User, AuthorizationLevel.Function, false)]
        [InlineData(AuthorizationLevel.Anonymous, AuthorizationLevel.Admin, false)]
        [InlineData(AuthorizationLevel.User, AuthorizationLevel.Anonymous, true)]
        public void Test(AuthorizationLevel principalLevel, AuthorizationLevel requiredLevel, bool expectSuccess)
        {
            ClaimsPrincipal principal = CreatePrincipal(principalLevel);
            bool result = AuthUtility.PrincipalHasAuthLevelClaim(principal, requiredLevel);

            Assert.Equal(expectSuccess, result);
        }

        private ClaimsPrincipal CreatePrincipal(AuthorizationLevel level)
        {
            var claims = new List<Claim>
                {
                    new Claim(SecurityConstants.AuthLevelClaimType, level.ToString())
                };

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }
    }
}
