// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Defines the supported Runtime drain scheduling modes.
/// </summary>
public enum RuntimeDrainMode
{
    /// <summary>
    /// Selects non-HTTP Workers in fixed batches and HTTP Workers at the fixed cutoff.
    /// </summary>
    Phased,

    /// <summary>
    /// Selects every linked Worker immediately.
    /// </summary>
    Immediate,
}
