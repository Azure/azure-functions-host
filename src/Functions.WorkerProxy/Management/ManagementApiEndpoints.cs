// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Text.Json;
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
/// The PUT handler explicitly uses ReadFromJsonAsync rather than automatic body binding so malformed JSON,
/// incompatible field types, and unsupported content types return our Host-aligned HTTP 400 InvalidBody
/// validation envelope. Automatic binding can reject requests before the handler runs with framework-owned
/// 400/415 responses that do not guarantee that envelope.
/// </remarks>
internal static class ManagementApiEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/worker/ready", ManagementApiHandlers.GetWorkerReady)
            .AddEndpointFilter(DisableCaching).AllowAnonymous();
        endpoints.MapPut("/admin/worker/assignment", AssignWorkerAsync).AllowAnonymous();
        endpoints.MapGet("/admin/worker/state", GetInstanceStateAsync)
            .AddEndpointFilter(DisableCaching).AllowAnonymous();
    }

    private static ValueTask<object?> DisableCaching(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return next(context);
    }

    private static async Task<IResult> AssignWorkerAsync(HttpRequest request, WorkerPodStateManager manager)
    {
        if (!request.HasJsonContentType())
        {
            return ManagementApiHandlers.InvalidBody();
        }

        WorkerAssignRequest? assignment;
        try
        {
            assignment = await request.ReadFromJsonAsync(
                WorkerProxyJsonContext.Default.WorkerAssignRequest, request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return ManagementApiHandlers.InvalidBody();
        }

        return ManagementApiHandlers.AssignWorker(assignment, manager);
    }

    private static async Task<IResult> GetInstanceStateAsync(HttpRequest request, WorkerPodStateManager manager)
    {
        long? lastKnownRevision = null;
        if (request.Query.TryGetValue("lastKnownRevision", out var revisions))
        {
            string? value = revisions.Count == 1 ? revisions[0] : null;
            // Accept invariant ASCII digits with an optional leading sign, not whitespace or JSON syntax.
            if (value is null
                || value.AsSpan().IndexOfAnyExcept("+-0123456789") >= 0
                || !long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long revision))
            {
                return ManagementApiHandlers.InvalidRevision();
            }

            lastKnownRevision = revision;
        }

        return await ManagementApiHandlers.GetInstanceStateAsync(lastKnownRevision, manager, request.HttpContext.RequestAborted);
    }
}
