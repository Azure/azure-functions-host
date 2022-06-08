using WorkerHarness.Core;
using WorkerHarness.Core.Worker;
using WorkerHarness.Core.WorkerProcess;

namespace WorkerHarness
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var workerDirectory = "C:\\temp\\FunctionApp1\\FunctionApp1\\bin\\Debug\\net6.0";

            IWorkerDescriptionBuilder workerDescriptionBuilder = new WorkerDescriptionBuilder();

            IWorkerProcessBuilder workerProcessBuilder = new WorkerProcessBuilder();

            WorkerHarnessExecutor harnessExecutor = new WorkerHarnessExecutor(workerDescriptionBuilder, workerProcessBuilder);

            Console.WriteLine(harnessExecutor.Start(workerDirectory));
        }
    }
}
