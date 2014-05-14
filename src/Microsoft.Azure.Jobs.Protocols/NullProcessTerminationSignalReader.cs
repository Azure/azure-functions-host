using System;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a null object implementation of <see cref="IProcessTerminationSignalReader"/>.</summary>
#if PUBLICPROTOCOL
    public class NullProcessTerminationSignalReader : IProcessTerminationSignalReader
#else
    internal class NullProcessTerminationSignalReader : IProcessTerminationSignalReader
#endif
    {
        /// <inheritdoc />
        public bool IsTerminationRequested(Guid hostInstanceId)
        {
            return false;
        }
    }
}
