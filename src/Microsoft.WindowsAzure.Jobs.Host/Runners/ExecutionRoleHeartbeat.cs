using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // health information written by an execution role. 
    internal class ExecutionRoleHeartbeat
    {
        // Function instance ID that we're currently processing. 
        public Guid? FunctionInstanceId { get; set; }

        // Times are UTC. 
        public DateTime Uptime { get; set; } // when this node went up
        public DateTime LastCacheReset { get; set; } // when were the caches last reset
        public DateTime Heartbeat { get; set; } // last time written

        public int RunCount { get; set; }
        public int CriticalErrors { get; set; }
        
    }
}
