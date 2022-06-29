// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace WorkerHarness.Core
{
    public class WorkerProcessBuilder : IWorkerProcessBuilder
    {
        /// <summary>
        /// Build an instance of a worker process
        /// </summary>
        /// <param name="workerDescription">a WorkerDescription object that contains path info about a language worker</param>
        /// <returns cref="Process"></returns>
        public Process Build(HarnessOptions workerOptions)
        {
            string workerId = Guid.NewGuid().ToString();
            string requestId = Guid.NewGuid().ToString();
            string workerExecutable = workerOptions.WorkerExecutable!;
            string arguments = $"{workerExecutable} --host {WorkerProcessConstants.DefaultHostUri} --port {WorkerProcessConstants.DefaultPort} --workerId {workerId} --requestId {requestId} --grpcMaxMessageLength {WorkerProcessConstants.GrpcMaxMessageLength}";
        
            string languageExecutable = workerOptions.LanguageExecutable!;

            var startInfo = new ProcessStartInfo(languageExecutable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                WorkingDirectory = workerOptions.WorkerDirectory,
                Arguments = arguments
            };

            Process process = new() { StartInfo = startInfo};

            return process;
        }

    }
}
