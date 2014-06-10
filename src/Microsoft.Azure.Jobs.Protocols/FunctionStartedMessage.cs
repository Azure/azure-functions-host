using System;
using System.Collections.Generic;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a message indicating that a function started executing.</summary>
    [JsonTypeName("FunctionStarted")]
#if PUBLICPROTOCOL
    public class FunctionStartedMessage : HostOutputMessage
#else
    internal class FunctionStartedMessage : HostOutputMessage
#endif
    {
        /// <summary>Gets or sets the function instance ID.</summary>
        public Guid FunctionInstanceId { get; set; }

        /// <summary>Gets or sets the function executing.</summary>
        public FunctionDescriptor Function { get; set; }

        /// <summary>Gets or sets the function's argument values.</summary>
        public IDictionary<string, string> Arguments { get; set; }

        /// <summary>Gets or sets the ID of the ancestor function instance.</summary>
        public Guid? ParentId { get; set; }

        /// <summary>Gets or sets the reason the function executed.</summary>
        public ExecutionReason Reason { get; set; }

        /// <summary>Gets or sets the time the function started executing.</summary>
        public DateTimeOffset StartTime { get; set; }

        /// <summary>Gets or sets the path of the blob containing console output from the function.</summary>
        public LocalBlobDescriptor OutputBlob { get; set; }

        /// <summary>Gets or sets the path of the blob containing per-parameter logging data.</summary>
        public LocalBlobDescriptor ParameterLogBlob { get; set; }
    }
}
