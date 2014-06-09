#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a function parameter log stored as text.</summary>
    [JsonTypeName("Text")]
#if PUBLICPROTOCOL
    public class TextParameterLog : ParameterLog
#else
    internal class TextParameterLog : ParameterLog
#endif
    {
        /// <summary>Gets or sets the log contents.</summary>
        public string Value { get; set; }
    }
}
