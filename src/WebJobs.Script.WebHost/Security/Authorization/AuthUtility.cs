// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization
{
    public class AuthUtility
    {
        private static IEnumerable<IAuthorizationRequirement> _requirements = new[] { new FunctionAuthorizationRequirement() };
        private static IEnumerable<string> _defaultAuthorizationSchemes = new[] { AuthLevelAuthenticationDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme };
        private static AuthorizationPolicy _defaultPolicy = new AuthorizationPolicy(_requirements, _defaultAuthorizationSchemes);

        public static AuthorizationPolicy DefaultFunctionPolicy => _defaultPolicy;

        public static AuthorizationPolicy CreateFunctionPolicy(IEnumerable<string> schemes = null)
        {
            schemes = schemes ?? _defaultAuthorizationSchemes;
            return new AuthorizationPolicy(_requirements, schemes);
        }

        public static bool PrincipalHasAuthLevelClaim(ClaimsPrincipal principal, AuthorizationLevel requiredLevel, string keyName = null)
        {
            // If the required auth level is anonymous, the requirement is met
            if (requiredLevel == AuthorizationLevel.Anonymous)
            {
                return true;
            }

            var claimLevels = principal
                .FindAll(SecurityConstants.AuthLevelClaimType)
                .Select(c => Enum.TryParse(c.Value, out AuthorizationLevel claimLevel) ? claimLevel : AuthorizationLevel.Anonymous)
                .ToArray();

            if (claimLevels.Length > 0)
            {
                // If we have a claim with Admin level, regardless of whether a name is required, return true.
                if (claimLevels.Any(claimLevel => claimLevel == AuthorizationLevel.Admin))
                {
                    return true;
                }

                // Ensure we match the expected level and key name, if one is required
                if (claimLevels.Any(l => l == requiredLevel) &&
                   (keyName == null || string.Equals(principal.FindFirstValue(SecurityConstants.AuthLevelKeyNameClaimType), keyName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
