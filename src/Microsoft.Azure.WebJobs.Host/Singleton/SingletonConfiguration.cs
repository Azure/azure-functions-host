// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Configuration options governing the lock functionality of <see cref="SingletonAttribute"/>.
    /// </summary>
    public sealed class SingletonConfiguration
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public SingletonConfiguration()
        {
            LockPeriod = TimeSpan.FromSeconds(15);
            LockAcquisitionTimeout = TimeSpan.FromMinutes(1);
            LockAcquisitionPollingInterval = TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// Gets or sets the default duration of a lock. As this period nears expiry,
        /// the lock will be automatically renewed.
        /// <remarks>
        /// Since there is auto-renewal, changing this to a value other than default
        /// will only change how often renewals occur.
        /// </remarks>
        /// </summary>
        public TimeSpan LockPeriod { get; set; }

        /// <summary>
        /// Gets or sets the timeout value for lock acquisition. If the lock for a
        /// particular function invocation is not obtained within this interval, the
        /// invocation will fail.
        /// </summary>
        public TimeSpan LockAcquisitionTimeout { get; set; }

        /// <summary>
        /// Gets or sets the polling interval governing how often retries are made
        /// when waiting to acquire a lock. The system will retry on this interval
        /// until the <see cref="LockAcquisitionTimeout"/> expiry is exceeded.
        /// </summary>
        public TimeSpan LockAcquisitionPollingInterval { get; set; }
    }
}
