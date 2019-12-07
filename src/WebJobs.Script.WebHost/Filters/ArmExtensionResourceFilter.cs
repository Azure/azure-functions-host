using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
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
    public sealed class ArmExtensionResourceFilter : IActionFilter
    {
        public bool AllowMultiple => false;

        public async Task<HttpResponseMessage> ExecuteActionFilterAsync(HttpActionContext actionContext, CancellationToken cancellationToken, Func<Task<HttpResponseMessage>> continuation)
        {
            // We only want to apply this filter for GET extension ARM requests that were forwarded directly to us via
            // hostruntime bridge, not hostruntime requests initiated internally by the geomaster. The latter requests
            // won't have the x-ms-arm-request-tracking-id header.
            var request = actionContext.Request;
            bool isArmExtensionRequest = request.HasHeader(ScriptConstants.AntaresARMRequestTrackingIdHeader) &&
                request.HasHeader(ScriptConstants.AntaresARMExtensionsRouteHeader);

            if (isArmExtensionRequest && request.Method == HttpMethod.Get)
            {
                // requests made by owner/co-admin are not filtered
                if (!request.HasHeaderValue(ScriptConstants.AntaresClientAuthorizationSourceHeader, "legacy"))
                {
                    var actionDescriptor = actionContext.ActionDescriptor as ReflectedHttpActionDescriptor;
                    if (actionDescriptor != null && actionDescriptor.MethodInfo != null &&
                        Utility.GetHierarchicalAttributeOrNull<ResourceContainsSecretsAttribute>(actionDescriptor.MethodInfo) != null)
                    {
                        // if the resource returned by the action contains secrets, fail the request
                        return request.CreateResponse(HttpStatusCode.Unauthorized, Resources.UnauthorizedArmExtensionResourceRequest);
                    }
                }
            }

            return await continuation();
        }
    }
}