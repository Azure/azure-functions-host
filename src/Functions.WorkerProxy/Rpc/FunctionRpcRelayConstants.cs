// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Rpc;

/// <summary>
/// Defines application-level FunctionRpc relay signals.
/// </summary>
internal static class FunctionRpcRelayConstants
{
    /// <summary>
    /// Identifies the response trailer that confirms WorkerProxy accepted sticky worker-removal ownership.
    /// </summary>
    public const string WorkerRemovalRequestedTrailerName = "x-ms-functions-worker-removal-requested";

    /// <summary>
    /// Identifies the affirmative worker-removal trailer value.
    /// </summary>
    public const string WorkerRemovalRequestedTrailerValue = "true";
}
