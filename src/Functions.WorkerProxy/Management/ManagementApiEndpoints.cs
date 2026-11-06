// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Registers worker lifecycle APIs on the management listener.
/// </summary>
/// <remarks>
/// <para>
/// Assignment and Worker startup are independent inputs: the platform may send assignment before the Worker
/// establishes its stream. Assignment is recorded as <c>Assigned</c> until a valid <c>StartStream</c> makes it
/// <c>Ready</c>; if the Worker is already ready, assignment enters <c>Ready</c> directly. Requiring Worker readiness
/// to accept assignment would impose the opposite startup ordering. Assignment success does not prove Worker
/// readiness, Runtime initialization, function loading, or HTTP serving readiness.
/// </para>
/// <para>
/// The supported platform sequence completes assignment before invoking Runtime's
/// <c>PUT /admin/workers/{workerId}</c> link endpoint. Worker startup may occur on either side of assignment.
/// Runtime linking establishes the stream used for <c>WorkerInitRequest</c>; an identified Runtime becomes
/// <c>Running</c>/<c>Linked</c> only after its matching successful initialization response is written to that stream.
/// The platform owns assignment-before-link ordering; the link endpoint does not enforce that precondition.
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>PUT /admin/worker/assignment</c>: <c>201</c> records a new assignment, with
/// <c>Location: /admin/worker/assignment</c>; <c>204</c> replays an equivalent assignment. Both have no response body.
/// Bound field-validation failures return <c>400</c> with an <c>errors</c> array containing
/// <c>Required</c> or <c>InvalidValue</c> codes and field targets. Lifecycle failures return
/// <c>409</c> with an <c>error</c> object containing <c>code</c> and <c>detail</c>:
/// <c>AssignmentConflict</c> for a different assignment or an app mismatch with an attached Runtime,
/// or <c>WorkerTerminated</c> for an equivalent retry after assigned-session failure.
/// Retry an uncertain outcome with the same assignment values; changing values is not a retry.
/// A failed assigned session requires pod replacement, not reassignment or a replacement stream on the same pod.
/// </description></item>
/// <item><description>
/// <c>POST /admin/infra/instanceState</c>: an absent or null previous checkpoint, or one differing from current
/// state, returns <c>200</c> with the current infrastructure checkpoint. An equal checkpoint polls for up to
/// 60 seconds; <c>204</c> means no change. Repeat with the last returned checkpoint after either result.
/// Nullable <c>functionGroup</c> may be omitted in the request and is explicitly emitted as null in the response.
/// </description></item>
/// </list>
/// <para>
/// Readiness and state are reported only by the authenticated infrastructure checkpoint; separate worker GET
/// readiness/state routes are not registered.
/// </para>
/// <para>
/// Platform callers are expected to send UTF-8 JSON. JSON-body endpoints use framework binding:
/// malformed or incompatible JSON with a supported charset returns <c>400</c>; unsupported media types return
/// <c>415</c>. Binding errors do not guarantee the field-validation envelope. Other binding failures follow
/// framework behavior. State/readiness responses disable caching, and request cancellation ends a pending poll.
/// </para>
/// </remarks>
internal static class ManagementApiEndpoints
{
    /// <summary>
    /// Registers the worker management and infrastructure instance-state routes.
    /// </summary>
    /// <param name="endpoints">The management listener's endpoint route builder.</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/admin/worker/assignment",
            (WorkerAssignRequest request, WorkerPodStateManager manager) => ManagementApiHandlers.AssignWorker(request, manager))
            .RequireAuthorization(ContainerManagementAuthenticationHandler.SchemeName);
        endpoints.MapPost("/admin/infra/instanceState",
            (WorkerOriginState? originState, WorkerPodStateManager manager, HttpContext context) =>
                ManagementApiHandlers.GetInstanceStateAsync(originState, manager, context.RequestAborted))
            .AddEndpointFilter(DisableResponseCaching).RequireAuthorization(ContainerManagementAuthenticationHandler.SchemeName);
    }

    private static ValueTask<object?> DisableResponseCaching(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return next(context);
    }
}
