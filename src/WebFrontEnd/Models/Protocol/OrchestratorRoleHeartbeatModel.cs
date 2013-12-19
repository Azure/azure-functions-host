using System;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Models.Protocol
{
    public class OrchestratorRoleHeartbeatModel
    {
        internal OrchestratorRoleHeartbeat UnderlyingObject { get; private set; }

        internal OrchestratorRoleHeartbeatModel(OrchestratorRoleHeartbeat underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public DateTime Heartbeat { get { return UnderlyingObject.Heartbeat; }}

        public DateTime LastCacheReset { get { return UnderlyingObject.LastCacheReset; } }

        public DateTime Uptime { get { return UnderlyingObject.Uptime; } }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}
