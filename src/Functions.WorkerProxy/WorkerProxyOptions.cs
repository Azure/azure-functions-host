// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Defines the WorkerProxy listener ports.
/// </summary>
internal sealed class WorkerProxyOptions
{
    /// <summary>
    /// The configuration section containing WorkerProxy settings.
    /// </summary>
    public const string SectionName = "WorkerProxy";

    /// <summary>
    /// Gets or sets the worker pod host name advertised to the runtime.
    /// </summary>
    public string PodName { get; set; } = Environment.MachineName;

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
    /// Gets or sets the runtime-facing HTTP forwarding listener port.
    /// </summary>
    public int HttpPort { get; set; } = 50055;

    /// <summary>
    /// Gets or sets an optional explicit worker HTTP destination.
    /// </summary>
    public string? WorkerHttpEndpoint { get; set; }
}
