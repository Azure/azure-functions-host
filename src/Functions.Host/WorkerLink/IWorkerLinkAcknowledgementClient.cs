// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Azure.Functions.WorkerProxy.Control;
using Grpc.Core;
using Grpc.Net.Client;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Acknowledges a successful Runtime link to WorkerProxy.
/// </summary>
public interface IWorkerLinkAcknowledgementClient
{
    /// <summary>
    /// Publishes the Runtime identity after Host link initialization succeeds.
    /// </summary>
    Task AcknowledgeAsync(
        Uri workerGrpcEndpoint,
        FunctionsSplitPodIdentity runtime,
        CancellationToken cancellationToken);
}

internal sealed class WorkerLinkAcknowledgementClient : IWorkerLinkAcknowledgementClient
{
    private static readonly TimeSpan AcknowledgementTimeout = TimeSpan.FromSeconds(10);

    public async Task AcknowledgeAsync(
        Uri workerGrpcEndpoint,
        FunctionsSplitPodIdentity runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerGrpcEndpoint);
        ArgumentNullException.ThrowIfNull(runtime);

        using GrpcChannel channel = GrpcChannel.ForAddress(workerGrpcEndpoint);
        WorkerLinkControl.WorkerLinkControlClient client = new(channel);
        try
        {
            await client.AcknowledgeLinkAsync(
                new AcknowledgeLinkRequest { Runtime = CreateRuntimeIdentity(runtime) },
                deadline: DateTime.UtcNow.Add(AcknowledgementTimeout),
                cancellationToken: cancellationToken);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.AlreadyExists)
        {
            throw new WorkerLinkException(
                WorkerLinkFailureReason.Conflict,
                "WorkerProxy rejected a conflicting Runtime identity.",
                exception);
        }
    }

    private static SplitPodIdentity CreateRuntimeIdentity(FunctionsSplitPodIdentity runtime)
    {
        FunctionsSplitFunctionAppIdentity app = runtime.App
            ?? throw new ArgumentException("The Runtime Function App identity is required.", nameof(runtime));

        return new()
        {
            ResourceNamespace = runtime.ResourceNamespace,
            PodName = runtime.PodName,
            PodGuid = runtime.PodGuid?.ToString("D"),
            LegionStampName = runtime.LegionStampName,
            PodType = runtime.PodType,
            App = new()
            {
                FunctionAppName = app.FunctionAppName,
                FunctionAppSiteId = app.FunctionAppSiteId ?? 0,
                AntaresStampName = app.AntaresStampName
            }
        };
    }
}
