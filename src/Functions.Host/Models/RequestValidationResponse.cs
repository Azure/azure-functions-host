// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Azure.Functions.Host.Models;

/// <summary>
/// Contains the validation errors returned with HTTP 400 for an invalid API request.
/// </summary>
/// <param name="Errors">The nonempty collection of detected field errors, or one request-body error.</param>
public sealed record RequestValidationResponse(
    [property: JsonProperty("errors")] IReadOnlyList<RequestValidationError> Errors);
