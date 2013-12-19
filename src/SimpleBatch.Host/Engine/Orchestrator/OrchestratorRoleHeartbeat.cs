using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class OrchestratorRoleHeartbeat
    {
        public DateTime Uptime { get; set; } // when this node went up
        public DateTime LastCacheReset { get; set; } // when were the caches last reset
        public DateTime Heartbeat { get; set; } // last scan time

        // ??? Add something about progress through listening on a large container?
    }
}
