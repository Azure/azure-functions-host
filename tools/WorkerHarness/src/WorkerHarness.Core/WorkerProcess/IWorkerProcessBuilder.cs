// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

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
        /// <param name="languageExecutable">the executable file of your language</param>
        /// <param name="workerExecutable">the executable file of your language worker</param>
        /// <param name="workerDirectory">the directory of your language worker</param>
        /// <returns cref="Process"></returns>
        Process Build(string languageExecutable, string workerExecutable, string workerDirectory);
    }
}
