#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a host version compatibility warning.</summary>
#if PUBLICPROTOCOL
    public class HostVersion
#else
    internal class HostVersion
#endif
    {
        /// <summary>Gets or sets a label describing the required feature.</summary>
        public string Label { get; set; }

        /// <summary>Gets or sets a link with more compatibility information.</summary>
        public string Link { get; set; }
    }
}
