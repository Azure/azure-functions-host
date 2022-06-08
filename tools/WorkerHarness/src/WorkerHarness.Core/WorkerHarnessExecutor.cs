using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkerHarness.Core.Worker;
using WorkerHarness.Core.WorkerProcess;

namespace WorkerHarness.Core
{
    public class WorkerHarnessExecutor
    {
        private IWorkerDescriptionBuilder _workerDescriptionBuilder;
        private IWorkerProcessBuilder _workerProcessBuilder;
        private string _workerId;

        public WorkerHarnessExecutor(IWorkerDescriptionBuilder workerDescriptionBuilder,
            IWorkerProcessBuilder workerProcessBuilder)
        {
            _workerDescriptionBuilder = workerDescriptionBuilder;
            _workerProcessBuilder = workerProcessBuilder;
            _workerId = Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Start a language worker process
        /// </summary>
        /// <param name="workerDirectory">an absolute path to the language worker folder</param>
        /// <returns>boolean whether the process starts successfully</returns>
        /// <exception cref="FileNotFoundException"></exception>
        public bool Start(string workerDirectory)
        {
            // find the worker.config.json file in the WorkerDirectory
            var workerConfigPath = Path.Combine(workerDirectory, WorkerConstants.WorkerConfigFileName);
            if (!File.Exists(workerConfigPath))
            {
                throw new FileNotFoundException($"The worker.config.json file is not found in {workerDirectory}");
            }

            WorkerDescription workerDescription = _workerDescriptionBuilder.Build(workerConfigPath, workerDirectory);

            string requestId = Guid.NewGuid().ToString();
            Process myProcess = _workerProcessBuilder.Build(workerDescription, _workerId, requestId);
            try
            {
                myProcess.Start();
                Console.WriteLine($"A {workerDescription.Language} is starting");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
        }
    }
}
