// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Attribute that can be applied at the class or function level to limit the
    /// execution time of job functions. To receive the timeout cancellation, a function
    /// must include a <see cref="CancellationToken"/> parameter. Then, if a particular
    /// function invocation exceeds the timeout, the <see cref="CancellationToken"/>
    /// will be cancelled, and an error will be logged. The function should monitor
    /// the token for cancellation and abort when it is cancelled, and it should pass
    /// it to any async operations it initiates.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class TimeoutAttribute : Attribute
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="timeout">The timeout limit as a <see cref="TimeSpan"/> string (e.g. "00:30:00").</param>
        public TimeoutAttribute(string timeout)
        {
            Timeout = TimeSpan.Parse(timeout, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets the timeout value.
        /// </summary>
        public TimeSpan Timeout { get; private set; }
    }
}
