// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using WorkerHarness.Core.Commons;

namespace WorkerHarness.Core.WorkerProcess
{
    public class WorkerProcessBuilder : IWorkerProcessBuilder
    {
        public IWorkerProcess Build(string languageExecutable, string workerExecutable, string workerDirectory)
        {
            string workerId = WorkerConstants.WorkerId;
            string requestId = Guid.NewGuid().ToString();
            string arguments = $"{workerExecutable} --host {HostConstants.DefaultHostUri} --port {HostConstants.DefaultPort} --workerId {workerId} --requestId {requestId} --grpcMaxMessageLength {HostConstants.GrpcMaxMessageLength}";
        
            var startInfo = new ProcessStartInfo(languageExecutable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                WorkingDirectory = workerDirectory,
                Arguments = arguments
            };

            Process process = new() { StartInfo = startInfo};

            return new SystemProcess(process);
        }

    }
}
