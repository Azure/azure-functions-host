// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
#endif
{
    /// <summary>Represents an Azure WebJobs SDK function.</summary>
#if PUBLICPROTOCOL
    public class FunctionDescriptor
#else
    internal class FunctionDescriptor
#endif
    {
        /// <summary>Gets or sets the ID of the function.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the fully qualified name of the function.</summary>
        public string FullName { get; set; }

        /// <summary>Gets or sets the display name of the function.</summary>
        public string ShortName { get; set; }

        /// <summary>Gets or sets the function's parameters.</summary>
        public IEnumerable<ParameterDescriptor> Parameters { get; set; }
    }
}
