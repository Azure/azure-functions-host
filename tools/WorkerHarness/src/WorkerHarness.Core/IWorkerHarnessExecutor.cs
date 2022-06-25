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
        /// Given a path to a scenario file, create a scenario object and execute it
        /// </summary>
        /// <param name="scenarioFile"></param>
        /// <returns></returns>
        Task<bool> Start(string scenarioFile);
    }
}