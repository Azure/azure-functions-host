// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Azure.Functions.WorkerProxy.Rpc;

/// <summary>
/// Finalizes the worker capabilities advertised to the runtime during normal initialization.
/// </summary>
internal interface IWorkerCapabilityFinalizer
{
    /// <summary>
    /// Updates capabilities on a relay-owned response copy before the relay freezes and forwards them.
    /// </summary>
    /// <param name="capabilities">The mutable capability map from the cloned successful initialization response.</param>
    /// <returns>The real worker HTTP destination, or <see langword="null"/> when HTTP proxying is unavailable.</returns>
    /// <remarks>
    /// Called once per session on its first successful worker initialization response, outside relay lifecycle locks.
    /// Failures propagate to session termination rather than forwarding a partially finalized response.
    /// Implementations must not retain or mutate the capability map after returning.
    /// </remarks>
    Uri? FinalizeCapabilities(IDictionary<string, string> capabilities);
}
