// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.ComputeSeparation.AppHost;

/// <summary>
/// Owns the runtime and its default worker pod, removing the pod before the runtime's network.
/// </summary>
internal sealed class ContainerTopology : IAsyncDisposable
{
    private readonly Lock _disposeLock = new();
    private Task? _disposeTask;

    public ContainerTopology(string repositoryRoot)
    {
        string prefix = $"functions-aspire-{Guid.NewGuid():N}";
        NetworkName = $"{prefix}-network";
        ProxyAlias = $"{prefix}-proxy-1";
        Runtime = new(repositoryRoot, "runtime.compose.yaml", $"{prefix}-runtime", new Dictionary<string, string>
        {
            ["COMPUTE_NETWORK_NAME"] = NetworkName,
            ["COMPUTE_HOST_PORT"] = "0"
        });
        WorkerPod = new(repositoryRoot, "worker-pod.compose.yaml", $"{prefix}-worker-1", new Dictionary<string, string>
        {
            ["COMPUTE_NETWORK_NAME"] = NetworkName,
            ["WORKER_PROXY_ALIAS"] = ProxyAlias,
            ["WORKER_PROXY_MANAGEMENT_PORT"] = "0",
            ["WORKER_ID"] = "cleanup",
            ["WORKER_REQUEST_ID"] = "cleanup"
        }, removeOnStop: true);
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
