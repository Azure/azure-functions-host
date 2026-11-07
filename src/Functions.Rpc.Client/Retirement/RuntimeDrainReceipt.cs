// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Confirms the active drain mode and immutable first acceptance time.
/// </summary>
/// <param name="DrainMode">The active drain mode.</param>
/// <param name="AcceptedAtUtc">The first acceptance time.</param>
public sealed record RuntimeDrainReceipt(
    RuntimeDrainMode DrainMode,
    DateTimeOffset AcceptedAtUtc);
