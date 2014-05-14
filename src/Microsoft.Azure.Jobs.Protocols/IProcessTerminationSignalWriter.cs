using System;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Defines a process termination request writer.</summary>
#if PUBLICPROTOCOL
    public interface IProcessTerminationSignalWriter
#else
    internal interface IProcessTerminationSignalWriter
#endif
    {
        /// <summary>Requests termination of a host instance process.</summary>
        /// <param name="hostInstanceId">The ID of the host instance.</param>
        void RequestTermination(Guid hostInstanceId);
    }
}
