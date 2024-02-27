// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Options
{
    /// <summary>
    /// Encapsulates required user arguments 
    /// </summary>
    public sealed class HarnessOptions
    {
        // Full path of a scenario file
        public string? ScenarioFile { get; set; }

        // Full path of the language executable (e.g. dotnet.exe)
        public string? LanguageExecutable { get; set; }

        // Language executable arguments
        public List<string> LanguageExecutableArguments { get; set; } = new List<string>();

        // Full path of the worker (e.g. worker.py)
        public string? WorkerPath { get; set; }

        // Full path to worker directory
        public string? WorkerDirectory { get; set; }

        // Worker arguments
        public List<string> WorkerArguments { get; set; } = new List<string>();

        // Full path of the Function App directory
        public string? FunctionAppDirectory { get; set; }

        // Optional flag to display verbose error messages to users
        public bool DisplayVerboseError { get; set; } = false;

        // Optional flag to display verbose error messages to users
        public bool ContinueUponFailure { get; set; } = false;

        // Wait time in seconds before exiting the harness process.
        public int WaitTimeInSecondsBeforeExit { get; set; }
    }
}
