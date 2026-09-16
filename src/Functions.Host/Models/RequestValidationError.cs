// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Host.Models;

/// <summary>
/// Describes a validation error in an API request.
/// </summary>
/// <param name="Code">The stable, case-sensitive validation error code.</param>
/// <param name="Target">The JSON field name, or <c>request</c> for a body-level error.</param>
public sealed record RequestValidationError(
    [property: JsonProperty("code")] string Code,
    [property: JsonProperty("target")] string Target);
