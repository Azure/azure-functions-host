// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;

namespace Azure.Functions.Host.Models;

/// <summary>
/// Confirms the accepted Runtime drain mode and its original start time.
/// </summary>
/// <param name="DrainMode">The active drain mode.</param>
/// <param name="AcceptedAtUtc">The immutable first acceptance time.</param>
public sealed record RuntimeDrainResponse(
    [property: JsonProperty("drainMode")] string DrainMode,
    [property: JsonProperty("acceptedAtUtc")] DateTimeOffset AcceptedAtUtc);
