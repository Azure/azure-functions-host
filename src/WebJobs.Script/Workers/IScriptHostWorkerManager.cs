// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers;

public interface IScriptHostWorkerManager
{
    WorkerManagerState State { get; }

    Task GetWorkerStatusesAsync();

    Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception);

    Task<IEnumerable<WorkerProcessInfo>> GetWorkerProcessInfoAsync(string workerRuntime);

    /// <summary>
    /// Gets the runtime information reported by currently initialized language workers.
    /// </summary>
    /// <param name="workerRuntime">The configured worker runtime.</param>
    /// <returns>The distinct worker-reported runtime information. Workers that have not reported metadata are excluded.</returns>
    IReadOnlyList<WorkerRuntimeInfo> GetWorkerRuntimeInfo(string workerRuntime)
    {
        return Array.Empty<WorkerRuntimeInfo>();
    }
}