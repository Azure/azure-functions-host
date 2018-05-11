// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
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
        [InlineData(new[] { AuthorizationLevel.Admin }, AuthorizationLevel.Function, true)]
        [InlineData(new[] { AuthorizationLevel.Function }, AuthorizationLevel.Function, true)]
        [InlineData(new[] { AuthorizationLevel.System }, AuthorizationLevel.Function, false)]
        [InlineData(new[] { AuthorizationLevel.User }, AuthorizationLevel.Function, false)]
        [InlineData(new[] { AuthorizationLevel.Anonymous }, AuthorizationLevel.Admin, false)]
        [InlineData(new[] { AuthorizationLevel.User }, AuthorizationLevel.Anonymous, true)]
        [InlineData(new[] { AuthorizationLevel.Admin, AuthorizationLevel.Anonymous }, AuthorizationLevel.Function, true)]
        [InlineData(new[] { AuthorizationLevel.Function, AuthorizationLevel.User }, AuthorizationLevel.Function, true)]
        [InlineData(new[] { AuthorizationLevel.System, AuthorizationLevel.User }, AuthorizationLevel.Function, false)]
        [InlineData(new[] { AuthorizationLevel.Anonymous, AuthorizationLevel.Function, AuthorizationLevel.System, AuthorizationLevel.User }, AuthorizationLevel.Admin, false)]
        [InlineData(new[] { AuthorizationLevel.User, AuthorizationLevel.Function }, AuthorizationLevel.Anonymous, true)]
        public void PrincipalHasAuthLevelClaim_WithRequiredLevel_ReturnsExpectedResult(AuthorizationLevel[] principalLevel, AuthorizationLevel requiredLevel, bool expectSuccess)
        {
            ClaimsPrincipal principal = CreatePrincipal(principalLevel);
            bool result = AuthUtility.PrincipalHasAuthLevelClaim(principal, requiredLevel);

            Assert.Equal(expectSuccess, result);
        }

        private ClaimsPrincipal CreatePrincipal(AuthorizationLevel[] levels)
        {
            var claims = levels.Select(l => new Claim(SecurityConstants.AuthLevelClaimType, l.ToString()));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }
    }
}
