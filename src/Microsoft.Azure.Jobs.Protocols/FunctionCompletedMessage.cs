using System;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a message indicating that a function completed execution.</summary>
    [JsonTypeName("FunctionCompleted")]
#if PUBLICPROTOCOL
    public class FunctionCompletedMessage : FunctionStartedMessage
#else
    internal class FunctionCompletedMessage : FunctionStartedMessage
#endif
    {
        /// <summary>Gets or sets the time the function stopped executing.</summary>
        public DateTimeOffset EndTime { get; set; }

        /// <summary>Gets or sets a value indicating whether the function completed successfully.</summary>
        public bool Succeeded { get; set; }

        /// <summary>
        /// Gets or sets the name of the exception type thrown by the function, if the function failed; otherwise,
        /// <see langword="null"/>.
        /// </summary>
        public string ExceptionType { get; set; }

        /// <summary>
        /// Gets or sets the exception message thrown by the function, if the function failed; otherwise,
        /// <see langword="null"/>.
        /// </summary>
        public string ExceptionMessage { get; set; }
    }
}
