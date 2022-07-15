// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    /// <summary>
    /// abtraction for a HarnessExecutor
    /// </summary>
    public interface IWorkerHarnessExecutor
    {
        /// <summary>
        /// create a scenario object and execute actions in the scenario object
        /// </summary>
        /// <returns></returns>
        Task<bool> StartAsync();
    }
}