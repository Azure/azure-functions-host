// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Describes the worker-pod lifecycle using Legion's split-worker status names.
/// </summary>
internal enum WorkerPodStatus
{
    /// <summary>Worker startup or assignment is incomplete.</summary>
    Starting,

    /// <summary>The assigned worker is ready.</summary>
    Running,

    /// <summary>The worker is retiring and may finish existing work but accept no new work.</summary>
    Draining,

    /// <summary>Worker removal has been requested; physical deletion is owned by the platform.</summary>
    RemovalRequested
}
