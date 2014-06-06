#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Defines a command that signals a heartbeat from a running host instance.</summary>
#if PUBLICPROTOCOL
    public interface IHeartbeatCommand
#else
    internal interface IHeartbeatCommand
#endif
    {
        /// <summary>Signals a heartbeat from a running host instance.</summary>
        void Beat();
    }
}
