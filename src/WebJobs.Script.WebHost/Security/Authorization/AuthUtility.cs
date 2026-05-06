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
        private static readonly IEnumerable<IAuthorizationRequirement> _requirements = new[] { new FunctionAuthorizationRequirement() };
        private static readonly IEnumerable<string> _defaultAuthorizationSchemes = new[] { AuthLevelAuthenticationDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme };
        private static readonly AuthorizationPolicy _defaultPolicy = new AuthorizationPolicy(_requirements, _defaultAuthorizationSchemes);

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

            var claimLevels = GetTrustedClaims(principal, SecurityConstants.AuthLevelClaimType)
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
                   (keyName == null || string.Equals(GetTrustedClaims(principal, SecurityConstants.AuthLevelKeyNameClaimType).FirstOrDefault()?.Value, keyName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool PrincipalHasInvokeClaim(ClaimsPrincipal principal, AuthorizationLevel requiredLevel)
        {
            // If the required auth level is anonymous, the requirement is met
            if (requiredLevel == AuthorizationLevel.Anonymous)
            {
                return true;
            }

            return PrincipalHasTrustedClaim(principal, SecurityConstants.InvokeClaimType, "true");
        }

        /// <summary>
        /// Returns true when the principal carries a claim (<paramref name="type"/> = <paramref name="value"/>) on
        /// an identity that was issued by one of the host's own authentication handlers — currently the key-based
        /// handler and the JWT bearer validator. Other identities on the principal are ignored.
        /// </summary>
        internal static bool PrincipalHasTrustedClaim(ClaimsPrincipal principal, string type, string value)
        {
            if (principal is null)
            {
                return false;
            }

            foreach (ClaimsIdentity identity in principal.Identities)
            {
                if (!IsTrustedHostIdentity(identity))
                {
                    continue;
                }

                foreach (Claim claim in identity.FindAll(type))
                {
                    if (string.Equals(claim.Value, value, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<Claim> GetTrustedClaims(ClaimsPrincipal principal, string type)
        {
            if (principal is null)
            {
                yield break;
            }

            foreach (ClaimsIdentity identity in principal.Identities)
            {
                if (!IsTrustedHostIdentity(identity))
                {
                    continue;
                }

                foreach (Claim claim in identity.FindAll(type))
                {
                    yield return claim;
                }
            }
        }

        private static bool IsTrustedHostIdentity(ClaimsIdentity identity)
        {
            // Authorization-level and invoke claims are only honored when the
            // identity carrying them was issued by one of the host's own
            // authentication handlers (the key-based handler or the JWT bearer
            // validator). Identities from other authentication schemes — for
            // example the EasyAuth identity built from x-ms-client-principal —
            // are not considered for these claims.
            return identity is not null &&
                string.Equals(identity.AuthenticationType, AuthLevelAuthenticationDefaults.AuthenticationScheme, StringComparison.Ordinal);
        }
    }
}
