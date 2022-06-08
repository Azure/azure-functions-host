using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkerHarness.Core.Worker;

namespace WorkerHarness.Core.WorkerProcess
{
    public class WorkerProcessBuilder : IWorkerProcessBuilder
    {
        /// <summary>
        /// Build an instance of a worker process
        /// </summary>
        /// <param name="workerDescription">a WorkerDescription object that contains path info about a language worker</param>
        /// <param name="workerId">a language worker Id</param>
        /// <param name="requestId">a request Id</param>
        /// <returns></returns>
        /// <exception cref="MissingMemberException"></exception>
        public Process Build(WorkerDescription workerDescription, string workerId, string requestId)
        {
            string workerFile = workerDescription.DefaultWorkerPath ?? throw new MissingMemberException("The default worker path is null");
            string arguments = $"{workerFile} --host {WorkerProcessConstants.DefaultHostUri} --port {WorkerProcessConstants.DefaultPort} --workerId {workerId} --requestId {requestId} --grpcMaxMessageLength {WorkerProcessConstants.GrpcMaxMessageLength}";
        
            string executableFile = workerDescription.DefaultExecutablePath ?? throw new MissingMemberException("The executable path is null");

            var startInfo = new ProcessStartInfo(executableFile)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                WorkingDirectory = workerDescription.WorkerDirectory,
                Arguments = arguments
            };

            Process process = new Process() { StartInfo = startInfo};

            return process;
        }

    }
}
