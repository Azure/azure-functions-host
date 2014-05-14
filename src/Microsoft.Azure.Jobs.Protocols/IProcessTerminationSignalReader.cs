using System;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Defines a process termination request reader.</summary>
#if PUBLICPROTOCOL
    public interface IProcessTerminationSignalReader
#else
    internal interface IProcessTerminationSignalReader
#endif
    {
        /// <summary>Determines whether process termination has been requested.</summary>
        /// <param name="hostInstanceId">The ID of the host instance.</param>
        /// <returns>
        /// <see langword="true"/> if process termination has been requested; otherwise, <see langword="false"/>.
        /// </returns>
        bool IsTerminationRequested(Guid hostInstanceId);
    }
}
