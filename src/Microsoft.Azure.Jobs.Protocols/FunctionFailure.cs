#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a function's execution failure information.</summary>
#if PUBLICPROTOCOL
    public class FunctionFailure
#else
    internal class FunctionFailure
#endif
    {
        /// <summary>Gets or sets the name of the type of exception that occurred.</summary>
        public string ExceptionType { get; set; }

        /// <summary>Gets or sets the details of the exception that occurred.</summary>
        public string ExceptionDetails { get; set; }
    }
}
