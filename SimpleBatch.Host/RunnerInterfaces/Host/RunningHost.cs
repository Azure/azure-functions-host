using System;
using System.Linq;

namespace RunnerInterfaces
{
    public class RunningHost
    {
        public static readonly TimeSpan HeartbeatSignalInterval = new TimeSpan(0, 0, 30);
        public static readonly TimeSpan HeartbeatPollInterval = new TimeSpan(0, 0, 45);

        public string RowKey { get; set; }
        public DateTime Timestamp { get; set; }

        // Provide more understandable names that indicate how the built-in properties are being used.

        public string AssemblyFullName
        {
            get
            {
                return RowKey;
            }
            set
            {
                RowKey = value;
            }
        }

        public DateTime LastHeartbeatUtc
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

            return DateTime.UtcNow < heartbeat.LastHeartbeatUtc.Add(RunningHost.HeartbeatPollInterval);
        }
    }
}
