// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
internal static class ManagementApiEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/worker/ready", ManagementApiHandlers.GetWorkerReady).AllowAnonymous();
        endpoints.MapPost("/admin/worker/assign", AssignWorkerAsync).AllowAnonymous();
        endpoints.MapPost("/admin/infra/instanceState", GetInstanceStateAsync).AllowAnonymous();
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
        if (!request.HasJsonContentType())
        {
            return ManagementApiHandlers.InvalidBody();
        }

        InstanceStatePollRequest? poll;
        try
        {
            poll = await request.ReadFromJsonAsync(
                WorkerProxyJsonContext.Default.InstanceStatePollRequest, request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return ManagementApiHandlers.InvalidBody();
        }

        return await ManagementApiHandlers.GetInstanceStateAsync(poll, manager, request.HttpContext.RequestAborted);
    }
}
