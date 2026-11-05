// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Defines the platform-visible lifecycle states of a worker proxy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkerStatus>))]
internal enum WorkerStatus
{
    /// <summary>
    /// The worker proxy is starting.
    /// </summary>
    Starting,

    /// <summary>
    /// The worker proxy is running.
    /// </summary>
    Running,

    /// <summary>
    /// The worker proxy is draining requests.
    /// </summary>
    Draining,

    /// <summary>
    /// Removal of the worker proxy has been requested.
    /// </summary>
    RemovalRequested
}
