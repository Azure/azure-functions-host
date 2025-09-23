using Microsoft.Azure.WebJobs.Script.Workers;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    internal sealed class TestWorkerRuntimeResolver : IWorkerRuntimeResolver
    {
        private readonly string _workerRuntime;
        
        internal TestWorkerRuntimeResolver(string workerRuntime)
        {
            _workerRuntime = workerRuntime;
        }

        public string GetWorkerRuntime(string defaultValue = null) => _workerRuntime;
    }
}
