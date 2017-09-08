// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using static Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.AuthUtility;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization
{
    public class AuthLevelAuthorizationHandler : AuthorizationHandler<AuthLevelRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthLevelRequirement requirement)
        {
            if (PrincipalHasAuthLevelClaim(context.User, requirement.Level))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
