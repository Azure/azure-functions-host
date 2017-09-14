// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
        public static IEnumerable<string> DefaultAuthorizationSchemes => new[]
        {
            AuthLevelAuthenticationDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme
        };

        public static AuthorizationPolicy CreateFunctionPolicy(IEnumerable<string> schemes = null)
        {
            schemes = schemes ?? DefaultAuthorizationSchemes;
            var requirements = new[] { new FunctionAuthorizationRequirement() };
            return new AuthorizationPolicy(requirements, schemes);
        }

        public static bool PrincipalHasAuthLevelClaim(ClaimsPrincipal principal, AuthorizationLevel requiredLevel, string keyName = null)
        {
            // If the required auth level is anonymous, the requirement is met
            if (requiredLevel == AuthorizationLevel.Anonymous)
            {
                return true;
            }

            AuthorizationLevel level = AuthorizationLevel.Anonymous;
            if (Enum.TryParse(principal.FindFirstValue(SecurityConstants.AuthLevelClaimType), out level))
            {
                // True if the identity is authenticated with Admin level, regardless of whether a name is required.
                if (level == AuthorizationLevel.Admin)
                {
                    return true;
                }

                // Ensure we match the expected level and key name, if one is required
                if (level == requiredLevel &&
                    (keyName == null || string.Equals(principal.FindFirstValue(SecurityConstants.AuthLevelKeyNameClaimType), keyName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
