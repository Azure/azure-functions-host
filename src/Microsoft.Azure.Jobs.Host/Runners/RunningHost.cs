using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs
{
    internal class RunningHost : TableEntity
    {
        public static readonly TimeSpan HeartbeatSignalInterval = new TimeSpan(0, 0, 30);
        public static readonly TimeSpan HeartbeatPollInterval = new TimeSpan(0, 0, 45);

        // Provide more understandable names that indicate how the built-in properties are being used.

        public Guid HostId
        {
            get
            {
                return Guid.Parse(RowKey);
            }
        }

        public DateTimeOffset LastHeartbeat
        {
            get
            {
                return Timestamp;
            }
        }

        internal static bool IsValidHeartbeat(RunningHost heartbeat)
        {
            if (heartbeat == null)
            {
                return false;
            }

            return DateTimeOffset.UtcNow < heartbeat.LastHeartbeat.Add(HeartbeatPollInterval);
        }
    }
}
