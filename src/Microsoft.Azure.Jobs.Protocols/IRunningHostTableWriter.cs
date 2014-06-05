using System;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Defines a table writer that signals a heartbeat from a running host.</summary>
#if PUBLICPROTOCOL
    public interface IRunningHostTableWriter
#else
    internal interface IRunningHostTableWriter
#endif
    {
        /// <summary>Signals a heartbeat from a running host.</summary>
        /// <param name="hostName">The name of the host or host instance.</param>
        void SignalHeartbeat(string hostName);
    }
}
