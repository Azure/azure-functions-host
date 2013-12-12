using System;
using Executor;

namespace WebFrontEnd.Models.Protocol
{
    public class ExecutionRoleHeartbeatModel
    {
        private ExecutionRoleHeartbeat UnderlyingObject { get; set; }
        internal ExecutionRoleHeartbeatModel(ExecutionRoleHeartbeat underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public DateTime Uptime { get { return UnderlyingObject.Uptime; } }

        public int RunCount { get { return UnderlyingObject.RunCount; } }
        public DateTime LastCacheReset { get { return UnderlyingObject.LastCacheReset; } }
        public DateTime Heartbeat { get { return UnderlyingObject.Heartbeat; } }
        public Guid? FunctionInstanceId { get { return UnderlyingObject.FunctionInstanceId; } }
        public int CriticalErrors { get { return UnderlyingObject.CriticalErrors; } }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}