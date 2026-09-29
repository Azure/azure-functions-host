// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Registers worker lifecycle APIs on the management listener.
/// </summary>
/// <remarks>
/// Platform callers are expected to send UTF-8 JSON. Assignment uses framework JSON binding:
/// malformed or incompatible JSON with a supported charset returns HTTP 400;
/// unsupported media types return HTTP 415. Binding errors do not guarantee our validation envelope.
/// Other binding failures follow framework behavior. Successfully bound requests use our
/// field-validation envelope and lifecycle error codes.
/// </remarks>
internal static class ManagementApiEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/worker/ready", ManagementApiHandlers.GetWorkerReady)
            .AddEndpointFilter(DisableResponseCaching).AllowAnonymous();
        endpoints.MapPut("/admin/worker/assignment",
            (WorkerAssignRequest request, WorkerPodStateManager manager) => ManagementApiHandlers.AssignWorker(request, manager))
            .AllowAnonymous();
        endpoints.MapGet("/admin/worker/state", ManagementApiHandlers.GetWorkerStateAsync)
            .AddEndpointFilter(DisableResponseCaching).AllowAnonymous();
    }

    private static ValueTask<object?> DisableResponseCaching(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return next(context);
    }
}
