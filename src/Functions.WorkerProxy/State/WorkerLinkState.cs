// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Defines the platform-visible states of a worker proxy's link to a runtime pod.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkerLinkState>))]
internal enum WorkerLinkState
{
    /// <summary>
    /// No runtime pod is linked.
    /// </summary>
    Unlinked,

    /// <summary>
    /// A link to a runtime pod is being established.
    /// </summary>
    Linking,

    /// <summary>
    /// A runtime pod is linked.
    /// </summary>
    Linked
}
