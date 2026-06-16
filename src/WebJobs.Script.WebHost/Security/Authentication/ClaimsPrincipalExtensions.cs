// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Security.Claims;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication
{
    public static class ClaimsPrincipalExtensions
    {
        public static bool IsFuncPlatform(this ClaimsPrincipal principal)
        {
            return principal.HasClaim(SecurityConstants.FuncPlatformClaimType, "true");
        }
    }
}
