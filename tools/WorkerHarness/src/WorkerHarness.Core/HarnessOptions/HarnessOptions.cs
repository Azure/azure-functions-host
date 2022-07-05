// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    /// <summary>
    /// Encapsulates required user arguments 
    /// </summary>
    public class HarnessOptions
    {
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

        /// <summary>
        /// Optional flag to display verbose error messages to users
        /// </summary>
        public bool DisplayVerboseError { get; set; } = false;

    }
}
