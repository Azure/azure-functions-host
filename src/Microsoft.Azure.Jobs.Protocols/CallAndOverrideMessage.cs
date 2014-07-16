// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a message to execute a function using override values for all parameters.</summary>
    [JsonTypeName("CallAndOverride")]
#if PUBLICPROTOCOL
    public class CallAndOverrideMessage : HostMessage
#else
    internal class CallAndOverrideMessage : HostMessage
#endif
    {
        /// <summary>Gets or sets the ID of this request.</summary>
        public Guid Id { get; set; }

        /// <summary>Gets or sets the ID of the function to run.</summary>
        public string FunctionId { get; set; }

        /// <summary>Gets or sets the arguments to the function.</summary>
        public IDictionary<string, string> Arguments { get; set; }

        /// <summary>Gets or sets the reason the function executed.</summary>
        public ExecutionReason Reason { get; set; }

        /// <summary>Gets or sets the ID of the parent function, if any.</summary>
        public Guid? ParentId { get; set; }
    }
}
