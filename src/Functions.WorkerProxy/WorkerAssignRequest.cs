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
    /// Function app name for signal identity.
    /// </summary>
    public required string FunctionAppName { get; set; }

    /// <summary>
    /// Function app identifier for signal identity.
    /// </summary>
    public required int FunctionAppId { get; set; }
    
    /// <summary>
    /// Function group name that defines the worker's inventory bucket.
    /// <c>"http"</c> is the explicit HTTP worker bucket; <c>""</c> or <c>null</c> is the default non-HTTP bucket.
    /// </summary>
    public required string FunctionGroupName { get; set; }
    
    public bool IsAlwaysReady { get; set; }
    
    /// <summary>
    /// Environment variables to apply to the worker process.
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Path to the customer function app directory. Defaults to <c>/home/site/wwwroot</c>.
    /// </summary>
    public string? FunctionAppDirectory { get; set; }
}
