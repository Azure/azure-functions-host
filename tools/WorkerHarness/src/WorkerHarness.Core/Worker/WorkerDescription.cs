// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    /// <summary>
    /// WorkerDescription encapsulates information about a language worker
    /// such as the language type, the executable path for that worker,
    /// the worker binary file location and the worker directory
    /// </summary>
    public class WorkerDescription
    {
        public string? Language { get; set; }

        public string? DefaultExecutablePath { get; set; }

        public string? DefaultWorkerPath { get; set; }

        public string? WorkerDirectory { get; set; }

        /// <summary>
        /// Convert DefaultExecutablePath and DefaultWorkerPath to absolute paths
        /// </summary>
        internal void UseAbsolutePaths()
        {   
            // Convert DefaultWorkerPath to absolute path
            if (!string.IsNullOrEmpty(WorkerDirectory) && !string.IsNullOrEmpty(DefaultWorkerPath))
            {
                DefaultWorkerPath = Path.Combine(WorkerDirectory, DefaultWorkerPath);
            }

            // Convert DefaultExecutablePath to an absolute path of the dotnet.exe file
            if (!string.IsNullOrEmpty(DefaultExecutablePath) 
                && DefaultExecutablePath.Equals(WorkerConstants.DotnetExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                ResolveDotnetExecutablePath();
            }

        }

        /// <summary>
        /// Set DefaultExecutablePath to be the absolute path of dotnet.exe
        /// </summary>
        private void ResolveDotnetExecutablePath()
        {
            var dotnetExecutablePath = Path.Combine(WorkerConstants.ProgramFilesFolder,
                                                    WorkerConstants.DotnetFolder,
                                                    WorkerConstants.DotnetExecutableFileName);
            if (File.Exists(dotnetExecutablePath))
            {
                DefaultExecutablePath = dotnetExecutablePath;
            }
        }

    }
}
