// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public interface IHttpWorkerChannel
    {
        string Id { get; }

        Task InvokeFunction(ScriptInvocationContext context);

        Task StartWorkerProcessAsync(CancellationToken cancellationToken = default);

        Task<WorkerStatus> GetWorkerStatusAsync();
    }
}
