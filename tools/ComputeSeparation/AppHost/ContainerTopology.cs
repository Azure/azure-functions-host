// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.ComputeSeparation.AppHost;

/// <summary>
/// Owns the runtime, emulator, and default worker pod, removing the pod before the runtime's network.
/// </summary>
internal sealed class ContainerTopology : IAsyncDisposable
{
    private readonly Lock _disposeLock = new();
    private Task? _disposeTask;

    public ContainerTopology(
        string repositoryRoot, string appAuthKey, string runtimeContainerKey, string runtimePodName,
        string workerId, string workerPodName, string workerRuntimeKey, string workerRequestId,
        string certificatePath, string certificateKeyPath, Func<ProcessStartInfo, Task>? runCommand = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appAuthKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeContainerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimePodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerPodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRuntimeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateKeyPath);

        string prefix = $"functions-aspire-{Guid.NewGuid():N}";
        NetworkName = $"{prefix}-network";
        ProxyAlias = $"{prefix}-proxy-1";
        Runtime = new(repositoryRoot, "runtime.compose.yaml", $"{prefix}-runtime", new Dictionary<string, string>
        {
            ["COMPUTE_NETWORK_NAME"] = NetworkName,
            ["COMPUTE_APP_AUTH_KEY"] = appAuthKey,
            ["COMPUTE_RUNTIME_CONTAINER_KEY"] = runtimeContainerKey,
            ["COMPUTE_RUNTIME_POD_NAME"] = runtimePodName,
            ["COMPUTE_HOST_PORT"] = "0",
            ["AZURITE_BLOB_PORT"] = "0",
            ["AZURITE_QUEUE_PORT"] = "0"
        }, runCommand: runCommand);
        WorkerPod = new(repositoryRoot, "worker-pod.compose.yaml", $"{prefix}-worker-1", new Dictionary<string, string>
        {
            ["COMPUTE_NETWORK_NAME"] = NetworkName,
            ["WORKER_PROXY_ALIAS"] = ProxyAlias,
            ["WORKER_POD_NAME"] = workerPodName,
            ["WORKER_RUNTIME_ACCESS_KEY"] = workerRuntimeKey,
            ["WORKER_APP_AUTH_KEY"] = appAuthKey,
            ["WORKER_PROXY_CERTIFICATE_PATH"] = certificatePath,
            ["WORKER_PROXY_CERTIFICATE_KEY_PATH"] = certificateKeyPath,
            ["WORKER_PROXY_MANAGEMENT_PORT"] = "0",
            ["WORKER_ID"] = workerId,
            ["WORKER_REQUEST_ID"] = workerRequestId
        }, removeOnStop: true, runCommand: runCommand);
    }

    public string NetworkName { get; }

    public string ProxyAlias { get; }

    public ComposeSession Runtime { get; }

    public ComposeSession WorkerPod { get; }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            return new(_disposeTask ??= ComposeSession.CleanupAsync(
                () => WorkerPod.DisposeAsync().AsTask(),
                () => Runtime.DisposeAsync().AsTask()));
        }
    }
}
