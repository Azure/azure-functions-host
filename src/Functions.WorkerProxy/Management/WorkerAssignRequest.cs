// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Describes assignment identity for an already-specialized worker.
/// </summary>
internal sealed class WorkerAssignRequest
{
    public string? FunctionAppName { get; init; }

    public string? FunctionGroupName { get; init; }

    public bool? IsAlwaysReady { get; init; }

    public Dictionary<string, string?>? Environment { get; init; }

    public string? FunctionAppDirectory { get; init; }
}
