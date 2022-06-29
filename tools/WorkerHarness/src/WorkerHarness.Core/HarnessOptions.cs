// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    /// <summary>
    /// WorkerDescription encapsulates information about a language worker
    /// such as the language type, the executable path for that worker,
    /// the worker binary file location and the worker directory
    /// </summary>
    public class HarnessOptions
    {
        public string? DefaultExecutablePath { get; set; }

        public string? DefaultWorkerPath { get; set; }

        /// <summary>
        /// Full path of the directory that contains the worker executable file
        /// </summary>
        public string? WorkerDirectory { get; set; }

        /// <summary>
        /// Full path of a scenario file
        /// </summary>
        public string? ScenarioFile { get; set; }

        /// <summary>
        /// Full path of the worker (or Function App) executable
        /// </summary>
        public string? WorkerExecutable { get; set; }

        /// <summary>
        /// Full path of the language executable (e.g. dotnet.exe)
        /// </summary>
        public string? LanguageExecutable { get; set; }

    }
}
