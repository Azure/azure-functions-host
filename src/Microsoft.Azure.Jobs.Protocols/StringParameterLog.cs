#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a function parameter log stored as text.</summary>
    [JsonTypeName("String")]
#if PUBLICPROTOCOL
    public class StringParameterLog : ParameterLog
#else
    internal class StringParameterLog : ParameterLog
#endif
    {
        /// <summary>Gets or sets the log contents.</summary>
        public string Value { get; set; }
    }
}
