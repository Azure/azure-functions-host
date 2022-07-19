// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.WorkerProcess
{
    /// <summary>
    /// an abstraction of a worker process
    /// </summary>
    public interface IWorkerProcess
    {
        bool HasExited { get; }
        bool Start();
        void WaitForProcessExit(int miliseconds);
        void Dispose();
    }
}
