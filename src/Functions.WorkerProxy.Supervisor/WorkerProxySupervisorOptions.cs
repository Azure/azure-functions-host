// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.WorkerProxy.Supervisor;

internal sealed class WorkerProxySupervisorOptions
{
    public const int DefaultMaxRestarts = 3;
    public static readonly TimeSpan DefaultShutdownGracePeriod = TimeSpan.FromSeconds(5);

    public static WorkerProxySupervisorOptions Default { get; } = new(
        workerProxyPath: "./Microsoft.Azure.Functions.WorkerProxy",
        maxRestarts: DefaultMaxRestarts,
        shutdownGracePeriod: DefaultShutdownGracePeriod);

    public WorkerProxySupervisorOptions(string workerProxyPath, int maxRestarts, TimeSpan shutdownGracePeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerProxyPath);

        if (maxRestarts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRestarts), maxRestarts, "Restart count must be non-negative.");
        }

        if (shutdownGracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(shutdownGracePeriod), shutdownGracePeriod, "Shutdown grace period must be non-negative.");
        }

        WorkerProxyPath = workerProxyPath;
        MaxRestarts = maxRestarts;
        ShutdownGracePeriod = shutdownGracePeriod;
    }

    public string WorkerProxyPath { get; }

    public int MaxRestarts { get; }

    public TimeSpan ShutdownGracePeriod { get; }
}
