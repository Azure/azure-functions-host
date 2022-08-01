// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.WorkerProcess
{
    /// <summary>
    /// an abtraction to build a worker process
    /// </summary>
    public interface IWorkerProcessBuilder
    {
        /// <summary>
        /// Build and return a worker process
        /// </summary>
        /// <param name="workerContext">encapsulate info to build a worker process</param>
        /// <returns cref="Process"></returns>
        IWorkerProcess Build(WorkerContext workerContext);
    }
}
