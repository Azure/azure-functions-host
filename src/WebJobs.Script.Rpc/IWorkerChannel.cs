// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public interface IWorkerChannel
    {
        string Id { get; }

        IWorkerProcess WorkerProcess { get; }

        Task<WorkerStatus> GetWorkerStatusAsync();

        Task StartWorkerProcessAsync(CancellationToken cancellationToken = default);
    }
}