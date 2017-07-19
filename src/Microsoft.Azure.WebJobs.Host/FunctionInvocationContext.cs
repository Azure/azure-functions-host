// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// The context for an executed filter or function
    /// </summary>
    public abstract class FunctionInvocationContext
    {
        /// <summary>Gets or sets the ID of the function.</summary>
        public Guid FunctionInstanceId { get; set; }

        /// <summary>Gets or sets the name of the function.</summary>
        public string FunctionName { get; set; }

        /// <summary>
        /// Gets or sets the function arguments
        /// </summary>
        public IReadOnlyDictionary<string, object> Arguments { get; set; }

        /// <summary>
        /// User properties
        /// </summary>
        public IDictionary<string, object> Properties { get; set; }

        /// <summary>
        /// Gets or sets the function logger
        /// </summary>
        public ILogger Logger { get; set; }

        /// <summary>
        /// gets or sets an object for invoking other functions
        /// </summary>
        internal IJobInvoker JobHost { get; set; }
    }
}