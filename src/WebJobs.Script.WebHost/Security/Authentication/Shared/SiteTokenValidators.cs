// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication.Shared
{
    /// <summary>
    /// JWT issuer/audience validators that compare against
    /// <see cref="TokenValidationParameters.ValidIssuers"/> /
    /// <see cref="TokenValidationParameters.ValidAudiences"/> using
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>.
    ///
    /// Shared (via linked source) between <c>WebJobs.Script.WebHost</c> and
    /// <c>Functions.WorkerProxy</c> so both projects accept the same
    /// NNA-minted tokens regardless of casing differences.
    /// </summary>
    internal static class SiteTokenValidators
    {
        public static string IssuerValidator(string issuer, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            if (validationParameters.ValidIssuers is null ||
                !validationParameters.ValidIssuers.Any(p => string.Equals(issuer, p, StringComparison.OrdinalIgnoreCase)))
            {
                throw new SecurityTokenInvalidIssuerException("IDX10205: Issuer validation failed.")
                {
                    InvalidIssuer = issuer,
                };
            }

            return issuer;
        }

        public static bool AudienceValidator(IEnumerable<string> audiences, SecurityToken securityToken, TokenValidationParameters validationParameters)
        {
            if (audiences is null || validationParameters.ValidAudiences is null)
            {
                return false;
            }

            foreach (string audience in audiences)
            {
                if (validationParameters.ValidAudiences.Any(p => string.Equals(audience, p, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
