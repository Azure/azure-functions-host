// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Defines the WorkerProxy pod identity and listener settings.
/// </summary>
internal sealed class WorkerProxyOptions
{
    private const int DefaultSystemLogQueueCapacity = 16_000;
    private static readonly TimeSpan DefaultSystemLogShutdownDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The configuration section containing WorkerProxy settings.
    /// </summary>
    public const string SectionName = "WorkerProxy";

    /// <summary>
    /// Gets or sets the required platform-provided pod name.
    /// </summary>
    public string PodName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the HTTP/1 management listener port.
    /// </summary>
    public int ManagementPort { get; set; } = 80;

    /// <summary>
    /// Gets or sets the runtime-facing HTTP/2 FunctionRpc listener port.
    /// </summary>
    public int RuntimeGrpcPort { get; set; } = 50053;

    /// <summary>
    /// Gets or sets the worker-facing HTTP/2 FunctionRpc listener port.
    /// </summary>
    public int WorkerGrpcPort { get; set; } = 50054;

    /// <summary>
    /// Gets or sets the runtime-facing HTTP/1 forwarding listener port.
    /// </summary>
    public int HttpPort { get; set; } = 28080;

    /// <summary>
    /// Gets or sets an optional explicit worker HTTP destination.
    /// </summary>
    public string? WorkerHttpEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the externally reachable HTTP origin advertised to the runtime.
    /// Required when the worker advertises HTTP proxying. Each WorkerProxy owns a dedicated
    /// address and port, so this URL must not include a path prefix. It must route to
    /// <see cref="HttpPort"/>, but its external port may differ because of platform port mapping.
    /// </summary>
    public string? HttpProxyEndpoint { get; set; }

    /// <summary>
    /// Gets or sets how Worker Proxy handles system logs received from a language worker.
    /// </summary>
    public WorkerSystemLogMode SystemLogMode { get; set; } = WorkerSystemLogMode.Disabled;

    /// <summary>
    /// Gets or sets the maximum number of worker system-log records buffered for stdout.
    /// </summary>
    public int SystemLogQueueCapacity { get; set; } = DefaultSystemLogQueueCapacity;

    /// <summary>
    /// Gets or sets how long shutdown waits for accepted worker system-log records to drain.
    /// </summary>
    public TimeSpan SystemLogShutdownDrainTimeout { get; set; } = DefaultSystemLogShutdownDrainTimeout;
}
