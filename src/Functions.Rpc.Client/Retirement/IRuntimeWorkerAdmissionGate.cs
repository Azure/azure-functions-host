// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Provides the irreversible Runtime worker-admission fence shared by retirement flows.
/// </summary>
public interface IRuntimeWorkerAdmissionGate
{
    /// <summary>
    /// Gets a value indicating whether the Runtime has permanently closed new Worker admission.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Permanently closes new Worker admission.
    /// </summary>
    /// <returns><see langword="true"/> when this call closed the gate; otherwise, <see langword="false"/>.</returns>
    bool TryClose();
}
