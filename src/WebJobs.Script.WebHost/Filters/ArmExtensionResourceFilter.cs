// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Filters
{
    /// <summary>
    /// Resource filter used to ensure secrets aren't returned for GET requests made via the Functions ARM extension
    /// API (hostruntime), unless properly authorized.
    /// </summary>
    /// <remarks>
    /// All our first class ARM APIs handle RBAC naturally. For the hostruntime bridge, the runtime collaborates
    /// based on request details coming from ARM/Geo.
    /// </remarks>
    public sealed class ArmExtensionResourceFilter : IAsyncResourceFilter
    {
        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            // We only want to apply this filter for GET extension ARM requests that were forwarded directly to us via
            // hostruntime bridge, not hostruntime requests initiated internally by the geomaster. The latter requests
            // won't have the x-ms-arm-request-tracking-id header.
            var request = context.HttpContext.Request;
            bool isArmExtensionRequest = request.HasHeader(ScriptConstants.AntaresARMRequestTrackingIdHeader) &&
                request.HasHeader(ScriptConstants.AntaresARMExtensionsRouteHeader);

            if (isArmExtensionRequest && string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                // requests made by owner/co-admin are not filtered
                if (!request.HasHeaderValue(ScriptConstants.AntaresClientAuthorizationSourceHeader, "legacy"))
                {
                    var controllerActionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;
                    if (controllerActionDescriptor != null && controllerActionDescriptor.MethodInfo != null &&
                        Utility.GetHierarchicalAttributeOrNull<ResourceContainsSecretsAttribute>(controllerActionDescriptor.MethodInfo) != null)
                    {
                        // if the resource returned by the action contains secrets, fail the request
                        context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.HttpContext.Response.WriteAsync(Resources.UnauthorizedArmExtensionResourceRequest);
                        return;
                    }
                }
            }

            await next();
        }
    }
}