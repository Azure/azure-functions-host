using System;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a running host's heartbeat.</summary>
#if PUBLICPROTOCOL
    [CLSCompliant(false)]
    public class RunningHost : TableEntity
#else
    internal class RunningHost : TableEntity
#endif
    {
        /// <summary>The interval at which heartbeats are signalled.</summary>
        public static readonly TimeSpan HeartbeatSignalInterval = new TimeSpan(0, 0, 30);

        /// <summary>The interval at which to poll for heartbeats.</summary>
        public static readonly TimeSpan HeartbeatPollInterval = new TimeSpan(0, 0, 45);

        // Provide more understandable names that indicate how the built-in properties are being used.

        /// <summary>Gets the host name.</summary>
        public string HostName
        {
            get
            {
                return RowKey;
            }
        }

        /// <summary>Gets the time of the host's last heartbeat.</summary>
        public DateTimeOffset LastHeartbeat
        {
            get
            {
                return Timestamp;
            }
        }

        /// <summary>Determines whether a host heartbeat is still valid.</summary>
        /// <param name="heartbeat"></param>
        /// <returns></returns>
        public static bool IsValidHeartbeat(RunningHost heartbeat)
        {
            if (heartbeat == null)
            {
                return false;
            }

            return DateTimeOffset.UtcNow < heartbeat.LastHeartbeat.Add(HeartbeatPollInterval);
        }
    }
}
