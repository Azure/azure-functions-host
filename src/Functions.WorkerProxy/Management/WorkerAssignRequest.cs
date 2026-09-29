// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Describes assignment identity and the caller-selected startup mode.
/// </summary>
/// <remarks>
/// All fields are required. The directory may be empty for Preconfigured but must be nonblank
/// for SpecializationRequired. Configuration is currently retained for retry comparison only.
/// </remarks>
internal sealed class WorkerAssignRequest
{
    /// <summary>
    /// Gets the required, case-sensitive startup mode selected by the caller.
    /// </summary>
    /// <remarks>
    /// <c>Preconfigured</c> means the worker starts with its final application configuration (BYOC).
    /// <c>SpecializationRequired</c> means the worker requires configuration through specialization.
    /// Assignment currently records either mode without performing specialization.
    /// The raw string is retained so invalid values can be reported as field-level validation errors.
    /// </remarks>
    public string? StartupMode { get; init; }

    public string? FunctionAppName { get; init; }

    public string? FunctionGroupName { get; init; }

    public bool? IsAlwaysReady { get; init; }

    public Dictionary<string, string?>? Environment { get; init; }

    public string? FunctionAppDirectory { get; init; }
}
