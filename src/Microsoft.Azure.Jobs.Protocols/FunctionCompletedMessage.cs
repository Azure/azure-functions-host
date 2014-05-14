#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a message indicating that a function completed execution.</summary>
    [JsonTypeName("FunctionCompleted")]
#if PUBLICPROTOCOL
    public class FunctionCompletedMessage : PersistentQueueMessage
#else
    internal class FunctionCompletedMessage : PersistentQueueMessage
#endif
    {
        /// <summary>Gets or sets the data about the function that completed execution.</summary>
        public FunctionCompletedSnapshot Snapshot { get; set; }
    }
}
