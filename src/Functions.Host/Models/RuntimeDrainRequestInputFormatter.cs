// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Host.Models;

/// <summary>
/// Uses System.Text.Json binding for Runtime drain without changing shared Host endpoints.
/// </summary>
internal sealed class RuntimeDrainRequestInputFormatter(JsonOptions options, ILogger<SystemTextJsonInputFormatter> logger)
    : SystemTextJsonInputFormatter(options, logger)
{
    /// <inheritdoc />
    protected override bool CanReadType(Type type) => type == typeof(RuntimeDrainRequest);
}
