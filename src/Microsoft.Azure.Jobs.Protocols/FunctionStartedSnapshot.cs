using System;
using System.Collections.Generic;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents information about a function instance that has started.</summary>
#if PUBLICPROTOCOL
    public class FunctionStartedSnapshot
#else
    internal class FunctionStartedSnapshot
#endif
    {
        /// <summary>Gets or sets the function instance ID.</summary>
        public Guid FunctionInstanceId { get; set; }

        /// <summary>Gets or sets the host instance ID.</summary>
        public Guid HostInstanceId { get; set; }

        /// <summary>Gets or sets the function ID.</summary>
        public string FunctionId { get; set; }

        /// <summary>Gets or sets the full name of the function.</summary>
        public string FunctionFullName { get; set; }

        /// <summary>Gets or sets the shortened display name of the function.</summary>
        public string FunctionShortName { get; set; }

        /// <summary>Gets or sets the function's arguments.</summary>
        public IDictionary<string, FunctionArgument> Arguments { get; set; }

        /// <summary>Gets or sets the ID of the ancestor function instance.</summary>
        public Guid? ParentId { get; set; }

        /// <summary>Gets or sets a description explaining why the function executed.</summary>
        public string Reason { get; set; }

        /// <summary>Gets or sets the time the function started executing.</summary>
        public DateTimeOffset StartTime { get; set; }

        /// <summary>Gets or sets the connection string for Azure Storage data.</summary>
        public string StorageConnectionString { get; set; }

        /// <summary>Gets or sets the connection string for Service Bus data.</summary>
        public string ServiceBusConnectionString { get; set; }

        /// <summary>Gets or sets the URL of the blob containing console output from the function.</summary>
        public string OutputBlobUrl { get; set; }

        /// <summary>Gets or sets the URL of the blob containing per-parameter logging data.</summary>
        public string ParameterLogBlobUrl { get; set; }

        /// <summary>Gets or sets the ID of the web job under which the function is running, if any.</summary>
        public WebJobRunIdentifier WebJobRunIdentifier { get; set; }
    }
}
