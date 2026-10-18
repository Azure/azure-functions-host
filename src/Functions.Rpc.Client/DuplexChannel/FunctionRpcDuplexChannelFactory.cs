// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates outbound FunctionRpc duplex channels.
/// </summary>
internal sealed class FunctionRpcDuplexChannelFactory : IDuplexChannelFactory<StreamingMessage>
{
    private readonly IRpcClientFactory _clientFactory;
    private readonly ILogger<FunctionRpcDuplexChannelFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionRpcDuplexChannelFactory"/> class.
    /// </summary>
    /// <param name="clientFactory">The factory that owns reusable endpoint channels.</param>
    /// <param name="logger">The logger used for partial connection cleanup failures.</param>
    public FunctionRpcDuplexChannelFactory(IRpcClientFactory clientFactory, ILogger<FunctionRpcDuplexChannelFactory> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DuplexChannel<StreamingMessage>> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        return await ConnectCoreAsync(endpoint, runtimeIdentity: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DuplexChannel<StreamingMessage>> ConnectAsync(
        Uri endpoint,
        RuntimePodIdentity runtimeIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeIdentity);

        return await ConnectCoreAsync(endpoint, runtimeIdentity, cancellationToken);
    }

    private async Task<DuplexChannel<StreamingMessage>> ConnectCoreAsync(
        Uri endpoint,
        RuntimePodIdentity runtimeIdentity,
        CancellationToken cancellationToken)
    {
        FunctionRpc.FunctionRpcClient client = await _clientFactory.CreateAsync(endpoint, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // This token only bounds channel creation. Disposing the returned channel cancels the long-lived RPC call.
        Metadata headers = runtimeIdentity is null
            ? null
            : [new(RuntimePodIdentity.MetadataName, JsonSerializer.SerializeToUtf8Bytes(
                runtimeIdentity, RuntimeIdentityJsonContext.Default.RuntimePodIdentity))];
        var call = client.EventStream(headers);
        try
        {
            return call.AsChannel();
        }
        catch
        {
            TryCleanup(call.Dispose, endpoint, "dispose the SDK duplex call");
            throw;
        }
    }

    private void TryCleanup(Action cleanup, Uri endpoint, string operation)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "FunctionRpc cleanup failed for endpoint {Endpoint} while attempting to {CleanupOperation}.",
                endpoint, operation);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RuntimePodIdentity))]
internal sealed partial class RuntimeIdentityJsonContext : JsonSerializerContext;
