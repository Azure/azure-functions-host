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

    internal static SplitPodIdentity CreateRuntimeIdentity(FunctionsSplitPodIdentity runtime)
    {
        FunctionsSplitFunctionAppIdentity app = runtime.App
            ?? throw new ArgumentException("The Runtime Function App identity is required.", nameof(runtime));

        return new()
        {
            ResourceNamespace = runtime.ResourceNamespace
                ?? throw new ArgumentException("The Runtime resource namespace is required.", nameof(runtime)),
            PodName = runtime.PodName
                ?? throw new ArgumentException("The Runtime pod name is required.", nameof(runtime)),
            PodGuid = runtime.PodGuid?.ToString("D")
                ?? throw new ArgumentException("The Runtime pod GUID is required.", nameof(runtime)),
            LegionStampName = runtime.LegionStampName
                ?? throw new ArgumentException("The Runtime Legion stamp name is required.", nameof(runtime)),
            PodType = runtime.PodType
                ?? throw new ArgumentException("The Runtime pod type is required.", nameof(runtime)),
            App = new()
            {
                FunctionAppName = app.FunctionAppName
                    ?? throw new ArgumentException("The Runtime Function App name is required.", nameof(runtime)),
                FunctionAppSiteId = app.FunctionAppSiteId
                    ?? throw new ArgumentException("The Runtime Function App site ID is required.", nameof(runtime)),
                AntaresStampName = app.AntaresStampName
                    ?? throw new ArgumentException("The Runtime Antares stamp name is required.", nameof(runtime))
            }
        };
    }
}
