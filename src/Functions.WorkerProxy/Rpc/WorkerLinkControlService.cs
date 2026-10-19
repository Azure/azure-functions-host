// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Control;
using Azure.Functions.WorkerProxy.State;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace Azure.Functions.WorkerProxy.Rpc;

internal sealed class WorkerLinkControlService(
    WorkerPodStateManager stateManager,
    WorkerProxyEndpointConfiguration endpoints) : WorkerLinkControl.WorkerLinkControlBase
{
    public override Task<AcknowledgeLinkResponse> AcknowledgeLink(
        AcknowledgeLinkRequest request,
        ServerCallContext context)
    {
        HttpContext httpContext = context.GetHttpContext();
        if (!endpoints.IsRuntimeGrpcPort(httpContext.Connection.LocalPort))
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, "Worker link control is unavailable on this listener."));
        }

        State.SplitPodIdentity runtime = ParseRuntimeIdentity(request.Runtime);
        RuntimeLinkAcknowledgementResult result = stateManager.AcknowledgeRuntimeLink(runtime);
        return result switch
        {
            RuntimeLinkAcknowledgementResult.Acknowledged or RuntimeLinkAcknowledgementResult.AlreadyAcknowledged =>
                Task.FromResult(new AcknowledgeLinkResponse()),
            RuntimeLinkAcknowledgementResult.Conflict =>
                throw new RpcException(new Status(StatusCode.AlreadyExists, "The Worker is already linked to another Runtime.")),
            RuntimeLinkAcknowledgementResult.NotPending =>
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "The Worker has no pending Runtime link.")),
            _ => throw new InvalidOperationException("Unexpected Runtime link acknowledgement result.")
        };
    }

    private static State.SplitPodIdentity ParseRuntimeIdentity(Control.SplitPodIdentity? request)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.ResourceNamespace)
            || string.IsNullOrWhiteSpace(request.PodName)
            || !Guid.TryParse(request.PodGuid, out Guid podGuid)
            || podGuid == Guid.Empty
            || string.IsNullOrWhiteSpace(request.LegionStampName)
            || !string.Equals(request.PodType, SplitPodTypes.Runtime, StringComparison.Ordinal)
            || request.App is null
            || string.IsNullOrWhiteSpace(request.App.FunctionAppName)
            || string.IsNullOrWhiteSpace(request.App.AntaresStampName))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A complete split Runtime identity is required."));
        }

        return new(
            request.ResourceNamespace,
            request.PodName,
            podGuid,
            request.LegionStampName,
            request.PodType,
            new(
                request.App.FunctionAppName,
                request.App.FunctionAppSiteId,
                request.App.AntaresStampName));
    }
}
