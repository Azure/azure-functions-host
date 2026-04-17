// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Request payload for <c>POST /admin/worker/assign</c> (worker specialization).
/// The worker proxy forwards these values to the language worker as a
/// <c>FunctionEnvironmentReloadRequest</c> over gRPC.
/// </summary>
internal sealed class WorkerAssignRequest
{
    /// <summary>
    /// Environment variables to apply to the worker process.
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Path to the customer function app directory. Defaults to <c>/home/site/wwwroot</c>.
    /// </summary>
    public string? FunctionAppDirectory { get; set; }
}
