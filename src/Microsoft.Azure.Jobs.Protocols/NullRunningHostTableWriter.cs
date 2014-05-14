using System;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a null object implementation of <see cref="IRunningHostTableWriter"/>.</summary>
#if PUBLICPROTOCOL
    public class NullRunningHostTableWriter : IRunningHostTableWriter
#else
    internal class NullRunningHostTableWriter : IRunningHostTableWriter
#endif
    {
        /// <inheritdoc />
        public void SignalHeartbeat(Guid hostOrInstanceId)
        {
        }
    }
}
