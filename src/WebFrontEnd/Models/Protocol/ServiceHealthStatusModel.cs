using System.Collections.Generic;
using System.Linq;


namespace Microsoft.WindowsAzure.Jobs.Dashboard.Models.Protocol
{
    public class ServiceHealthStatusModel
    {
        internal ServiceHealthStatus UnderlyingObject { get; private set; }

        internal ServiceHealthStatusModel(ServiceHealthStatus underlyingObject)
        {
            UnderlyingObject = underlyingObject;
            Executors = underlyingObject.Executors.ToDictionary(kvp => kvp.Key, kvp => new ExecutionRoleHeartbeatModel(kvp.Value));
            Orchestrator = new OrchestratorRoleHeartbeatModel(underlyingObject.Orchestrator);
        }

        public OrchestratorRoleHeartbeatModel Orchestrator { get; private set; }

        public IDictionary<string, ExecutionRoleHeartbeatModel> Executors { get; private set; }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}