// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Context class for <see cref="IFunctionInvocationFilter"/>>
    /// </summary>
    public class FunctionExecutedContext : FunctionInvocationContext
    {
        /// <summary>
        /// Gets or sets the function result
        /// </summary>
        public FunctionResult FunctionResult { get; set; }
    }
}