// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Base context class for <see cref="IFunctionInvocationFilter"/> context objects.
    /// </summary>
    public abstract class FunctionInvocationContext
    {
        /// <summary>
        /// Gets or sets the function instance ID.
        /// </summary>
        public Guid FunctionInstanceId { get; set; }

        /// <summary>
        /// Gets or sets the name of the function.
        /// </summary>
        public string FunctionName { get; set; }

        /// <summary>
        /// Gets or sets the function arguments.
        /// </summary>
        public IReadOnlyDictionary<string, object> Arguments { get; set; }

        /// <summary>
        /// Gets or sets the property bag.
        /// </summary>
        public IDictionary<string, object> Properties { get; set; }

        /// <summary>
        /// Gets or sets the logger.
        /// </summary>
        public ILogger Logger { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="IJobInvoker"/>.
        /// </summary>
        internal IJobInvoker Invoker { get; set; }
    }
}