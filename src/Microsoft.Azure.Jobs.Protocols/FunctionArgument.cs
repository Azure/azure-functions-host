#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents an argument to a function.</summary>
#if PUBLICPROTOCOL
    public class FunctionArgument
#else
    internal class FunctionArgument
#endif
    {
        /// <summary>Gets or sets the argument's parameter type.</summary>
        public ParameterDescriptor ParameterType { get; set; }

        /// <summary>Gets or sets the argument's value.</summary>
        public string Value { get; set; }
    }
}
