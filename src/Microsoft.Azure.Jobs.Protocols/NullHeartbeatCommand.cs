#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a null object implementation of <see cref="IHeartbeatCommand"/>.</summary>
#if PUBLICPROTOCOL
    public class NullHeartbeatCommand : IHeartbeatCommand
#else
    internal class NullHeartbeatCommand : IHeartbeatCommand
#endif
    {
        /// <inheritdoc />
        public void Beat()
        {
        }
    }
}
