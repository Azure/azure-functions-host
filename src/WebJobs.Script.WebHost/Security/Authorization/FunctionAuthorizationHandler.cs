// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Script.Description;
using static Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.AuthUtility;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization
{
    public class FunctionAuthorizationHandler : AuthorizationHandler<FunctionAuthorizationRequirement, FunctionDescriptor>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, FunctionAuthorizationRequirement requirement, FunctionDescriptor resource)
        {
            var httpTrigger = resource.HttpTriggerAttribute;
            if (httpTrigger != null && PrincipalHasAuthLevelClaim(context.User, httpTrigger.AuthLevel))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
