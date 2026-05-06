// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Security.Claims;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Security
{
    public class AuthenticationLevelHandlerTests
    {
        [Fact]
        public void SanitizeEasyAuthIdentity_StripsAuthLevelClaim()
        {
            var input = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "alice"),
                    new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString()),
                },
                authenticationType: "easyauth");

            ClaimsIdentity sanitized = AuthenticationLevelHandler.SanitizeEasyAuthIdentity(input);

            Assert.DoesNotContain(sanitized.Claims, c => string.Equals(c.Type, SecurityConstants.AuthLevelClaimType));
            Assert.Contains(sanitized.Claims, c => string.Equals(c.Type, ClaimTypes.Name) && string.Equals(c.Value, "alice"));
        }

        [Fact]
        public void SanitizeEasyAuthIdentity_StripsInvokeClaim()
        {
            var input = new ClaimsIdentity(
                new[]
                {
                    new Claim(SecurityConstants.InvokeClaimType, "true"),
                },
                authenticationType: "easyauth");

            ClaimsIdentity sanitized = AuthenticationLevelHandler.SanitizeEasyAuthIdentity(input);

            Assert.DoesNotContain(sanitized.Claims, c => string.Equals(c.Type, SecurityConstants.InvokeClaimType));
        }

        [Fact]
        public void SanitizeEasyAuthIdentity_StripsKeyNameClaim()
        {
            var input = new ClaimsIdentity(
                new[]
                {
                    new Claim(SecurityConstants.AuthLevelKeyNameClaimType, "host"),
                },
                authenticationType: "easyauth");

            ClaimsIdentity sanitized = AuthenticationLevelHandler.SanitizeEasyAuthIdentity(input);

            Assert.DoesNotContain(sanitized.Claims, c => string.Equals(c.Type, SecurityConstants.AuthLevelKeyNameClaimType));
        }

        [Fact]
        public void SanitizeEasyAuthIdentity_StripsAssignUnencryptedClaim()
        {
            var input = new ClaimsIdentity(
                new[]
                {
                    new Claim(SecurityConstants.AssignUnencryptedClaimType, "true"),
                },
                authenticationType: "easyauth");

            ClaimsIdentity sanitized = AuthenticationLevelHandler.SanitizeEasyAuthIdentity(input);

            Assert.DoesNotContain(sanitized.Claims, c => string.Equals(c.Type, SecurityConstants.AssignUnencryptedClaimType));
        }

        [Fact]
        public void SanitizeEasyAuthIdentity_PreservesNonPrivilegedClaims()
        {
            var input = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "user-id"),
                    new Claim(ClaimTypes.Email, "alice@example.com"),
                    new Claim(ClaimTypes.Role, "Reader"),
                },
                authenticationType: "easyauth");

            ClaimsIdentity sanitized = AuthenticationLevelHandler.SanitizeEasyAuthIdentity(input);

            Assert.Equal(3, sanitized.Claims.Count());
            Assert.Contains(sanitized.Claims, c => string.Equals(c.Type, ClaimTypes.Email));
            Assert.Contains(sanitized.Claims, c => string.Equals(c.Type, ClaimTypes.Role));
            Assert.Contains(sanitized.Claims, c => string.Equals(c.Type, ClaimTypes.NameIdentifier));
        }

        [Fact]
        public void SanitizeEasyAuthIdentity_PreservesAuthenticationTypeAndNameClaimType()
        {
            var input = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "alice") },
                authenticationType: "aad",
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);

            ClaimsIdentity sanitized = AuthenticationLevelHandler.SanitizeEasyAuthIdentity(input);

            Assert.Equal("aad", sanitized.AuthenticationType);
            Assert.Equal(ClaimTypes.Name, sanitized.NameClaimType);
            Assert.Equal(ClaimTypes.Role, sanitized.RoleClaimType);
            Assert.Equal("alice", sanitized.Name);
        }
    }
}
