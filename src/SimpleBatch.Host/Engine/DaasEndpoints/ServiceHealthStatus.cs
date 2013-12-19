using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class ServiceHealthStatus
    {
        public IDictionary<string, ExecutionRoleHeartbeat> Executors { get; set; }

        public OrchestratorRoleHeartbeat Orchestrator { get; set; }
    }
}
