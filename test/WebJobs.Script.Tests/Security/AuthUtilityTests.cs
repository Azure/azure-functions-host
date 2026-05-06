// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
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
            ClaimsPrincipal principal = CreateTrustedPrincipal(principalLevel);
            bool result = AuthUtility.PrincipalHasAuthLevelClaim(principal, requiredLevel);

            Assert.Equal(expectSuccess, result);
        }

        [Fact]
        public void PrincipalHasAuthLevelClaim_AdminClaimOnUntrustedIdentity_ReturnsFalse()
        {
            // An identity that did not come from one of the host's authentication
            // handlers (e.g. an EasyAuth identity built off the request header)
            // must not satisfy the auth-level requirement.
            var untrustedIdentity = new ClaimsIdentity(
                new[] { new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString()) },
                authenticationType: "easyauth");

            var principal = new ClaimsPrincipal(untrustedIdentity);

            Assert.False(AuthUtility.PrincipalHasAuthLevelClaim(principal, AuthorizationLevel.Admin));
            Assert.False(AuthUtility.PrincipalHasAuthLevelClaim(principal, AuthorizationLevel.Function));
        }

        [Fact]
        public void PrincipalHasAuthLevelClaim_AdminClaimOnNullAuthenticationType_ReturnsFalse()
        {
            // An identity with no AuthenticationType (e.g. an unauthenticated
            // principal) is not produced by any of the host authentication
            // handlers and must not satisfy the auth-level requirement.
            var anonymous = new ClaimsIdentity(
                new[] { new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString()) });

            var principal = new ClaimsPrincipal(anonymous);

            Assert.False(AuthUtility.PrincipalHasAuthLevelClaim(principal, AuthorizationLevel.Admin));
        }

        [Fact]
        public void PrincipalHasAuthLevelClaim_KeyNameRequired_OnlyConsidersTrustedIdentities()
        {
            // The trusted identity has Function level for keyName "host"; an
            // additional identity from a non-host scheme provides keyName
            // "function". The two identities must not be combined when matching
            // the (level, keyName) requirement.
            var trusted = new ClaimsIdentity(
                new[]
                {
                    new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Function.ToString()),
                    new Claim(SecurityConstants.AuthLevelKeyNameClaimType, "host"),
                },
                AuthLevelAuthenticationDefaults.AuthenticationScheme);

            var untrusted = new ClaimsIdentity(
                new[] { new Claim(SecurityConstants.AuthLevelKeyNameClaimType, "function") },
                authenticationType: "easyauth");

            var principal = new ClaimsPrincipal(new[] { trusted, untrusted });

            Assert.False(AuthUtility.PrincipalHasAuthLevelClaim(principal, AuthorizationLevel.Function, keyName: "function"));
            Assert.True(AuthUtility.PrincipalHasAuthLevelClaim(principal, AuthorizationLevel.Function, keyName: "host"));
        }

        [Fact]
        public void PrincipalHasInvokeClaim_OnTrustedIdentity_ReturnsTrue()
        {
            var trusted = new ClaimsIdentity(
                new[] { new Claim(SecurityConstants.InvokeClaimType, "true") },
                AuthLevelAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(trusted);

            Assert.True(AuthUtility.PrincipalHasInvokeClaim(principal, AuthorizationLevel.Function));
        }

        [Fact]
        public void PrincipalHasInvokeClaim_OnUntrustedIdentity_ReturnsFalse()
        {
            // An invoke=true claim on an identity that is not produced by a host
            // authentication handler must not grant invoke.
            var untrusted = new ClaimsIdentity(
                new[] { new Claim(SecurityConstants.InvokeClaimType, "true") },
                authenticationType: "easyauth");

            var principal = new ClaimsPrincipal(untrusted);

            Assert.False(AuthUtility.PrincipalHasInvokeClaim(principal, AuthorizationLevel.Function));
        }

        [Fact]
        public void PrincipalHasInvokeClaim_AnonymousLevel_AlwaysTrue()
        {
            Assert.True(AuthUtility.PrincipalHasInvokeClaim(new ClaimsPrincipal(), AuthorizationLevel.Anonymous));
        }

        [Fact]
        public void PrincipalHasTrustedClaim_OnUntrustedIdentity_ReturnsFalse()
        {
            var untrusted = new ClaimsIdentity(
                new[] { new Claim(SecurityConstants.AssignUnencryptedClaimType, "true") },
                authenticationType: "easyauth");

            var principal = new ClaimsPrincipal(untrusted);

            Assert.False(AuthUtility.PrincipalHasTrustedClaim(principal, SecurityConstants.AssignUnencryptedClaimType, "true"));
        }

        [Fact]
        public void PrincipalHasTrustedClaim_OnTrustedIdentity_ReturnsTrue()
        {
            var trusted = new ClaimsIdentity(
                new[] { new Claim(SecurityConstants.AssignUnencryptedClaimType, "true") },
                AuthLevelAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(trusted);

            Assert.True(AuthUtility.PrincipalHasTrustedClaim(principal, SecurityConstants.AssignUnencryptedClaimType, "true"));
        }

        private static ClaimsPrincipal CreateTrustedPrincipal(AuthorizationLevel[] levels)
        {
            IEnumerable<Claim> claims = levels.Select(l => new Claim(SecurityConstants.AuthLevelClaimType, l.ToString()));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthLevelAuthenticationDefaults.AuthenticationScheme));
        }
    }
}
