// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Attribute used to specify <see cref="TraceLevel"/> for job functions.
    /// This setting will affect both Console logging as well as Dashboard logging.
    /// </summary>
    /// <remarks>
    /// For very high throughput functions where performance is key you can set the
    /// level to <see cref="TraceLevel.Off"/> if you don't want any logs (Console or
    /// Dashboard) to be produced. Dashboard logging is somewhat expensive, so this
    /// can improve your throughput. You can also choose to set the level to
    /// <see cref="TraceLevel.Error"/> and logs will only be written for function
    /// invocations that fail.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class TraceLevelAttribute : Attribute
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="level">The <see cref="TraceLevel"/> to use for the function.</param>
        public TraceLevelAttribute(TraceLevel level)
        {
            Level = level;
        }

        /// <summary>
        /// Gets the <see cref="TraceLevel"/> to use for the function.
        /// </summary>
        public TraceLevel Level { get; private set; }
    }
}
