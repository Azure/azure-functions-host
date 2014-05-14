#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a message indicating that a function started executing.</summary>
    [JsonTypeName("FunctionStarted")]
#if PUBLICPROTOCOL
    public class FunctionStartedMessage : PersistentQueueMessage
#else
    internal class FunctionStartedMessage : PersistentQueueMessage
#endif
    {
        /// <summary>Gets or sets the data about the function that started executing.</summary>
        public FunctionStartedSnapshot Snapshot { get; set; }
    }
}
