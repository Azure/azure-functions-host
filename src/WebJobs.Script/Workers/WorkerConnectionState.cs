// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Script.Workers;

/// <summary>
/// Represents the connection lifecycle state of an external worker.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum WorkerConnectionState
{
    /// <summary>
    /// The gRPC connection and init handshake are in progress.
    /// </summary>
    Connecting,

    /// <summary>
    /// The worker is fully connected and ready for invocations.
    /// </summary>
    Connected,

    /// <summary>
    /// Deallocation has been requested. In-flight invocations are draining.
    /// </summary>
    Draining,

    /// <summary>
    /// The connection has been closed and the channel removed.
    /// </summary>
    Disconnected,

    /// <summary>
    /// The connection attempt failed.
    /// </summary>
    Error
}
