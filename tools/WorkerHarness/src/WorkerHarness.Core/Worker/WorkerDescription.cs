using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core.Worker
{
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
