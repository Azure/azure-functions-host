// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Contains a worker link admission or initialization error. Validation failures use a separate errors array.
/// </summary>
/// <param name="Error">The failure code and optional diagnostic detail.</param>
public sealed record WorkerLinkErrorResponse(
    [property: JsonProperty("error")] WorkerLinkError Error);
