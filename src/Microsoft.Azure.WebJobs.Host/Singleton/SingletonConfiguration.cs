// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Configuration options governing the lock functionality of <see cref="SingletonAttribute"/>.
    /// </summary>
    public sealed class SingletonConfiguration
    {
        private TimeSpan _lockPeriod;
        private TimeSpan _lockAcquisitionTimeout;
        private TimeSpan _lockAcquisitionPollingInterval;
        private TimeSpan _listenerLockRecoveryPollingInterval;

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public SingletonConfiguration()
        {
            _lockPeriod = TimeSpan.FromSeconds(15);
            _lockAcquisitionTimeout = TimeSpan.FromMinutes(1);
            _lockAcquisitionPollingInterval = TimeSpan.FromSeconds(1);
            _listenerLockRecoveryPollingInterval = TimeSpan.FromMinutes(1);
        }

        /// <summary>
        /// Gets or sets the default duration of a lock. As this period nears expiry,
        /// the lock will be automatically renewed.
        /// <remarks>
        /// Since there is auto-renewal, changing this to a value other than default
        /// will only change how often renewals occur.
        /// </remarks>
        /// </summary>
        public TimeSpan LockPeriod
        {
            get
            {
                return _lockPeriod;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(15) ||
                    value > TimeSpan.FromSeconds(60))
                {
                    throw new ArgumentOutOfRangeException("value");
                }
                _lockPeriod = value;
            }
        }

        /// <summary>
        /// Gets or sets the timeout value for lock acquisition. If the lock for a
        /// particular function invocation is not obtained within this interval, the
        /// invocation will fail.
        /// </summary>
        public TimeSpan LockAcquisitionTimeout
        {
            get
            {
                return _lockAcquisitionTimeout;
            }
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException("value");
                }
                _lockAcquisitionTimeout = value;
            }
        }

        /// <summary>
        /// Gets or sets the polling interval governing how often retries are made
        /// when waiting to acquire a lock. The system will retry on this interval
        /// until the <see cref="LockAcquisitionTimeout"/> expiry is exceeded.
        /// </summary>
        public TimeSpan LockAcquisitionPollingInterval 
        {
            get
            {
                return _lockAcquisitionPollingInterval;
            }
            set
            {
                if (value < TimeSpan.FromMilliseconds(500))
                {
                    throw new ArgumentOutOfRangeException("value");
                }
                _lockAcquisitionPollingInterval = value;
            }
        }

        /// <summary>
        /// Gets or sets the polling interval used by singleton locks of type
        /// <see cref="SingletonMode.Listener"/> to periodically reattempt to
        /// acquire their lock if they failed to acquire it on startup.
        /// </summary>
        /// <remarks>
        /// On startup, singleton listeners for triggered functions will each attempt to
        /// acquire their locks. If they are unable to within the timeout window, the
        /// listener won't start (and the triggered function won't be running). However,
        /// behind the scenes, the listener will periodically reattempt to acquire the lock
        /// based on this value.
        /// To disable this behavior, set the value to <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </remarks>
        public TimeSpan ListenerLockRecoveryPollingInterval
        {
            get
            {
                return _listenerLockRecoveryPollingInterval;
            }
            set
            {
                if (value != Timeout.InfiniteTimeSpan &&
                    value < TimeSpan.FromSeconds(15))
                {
                    throw new ArgumentOutOfRangeException("value");
                }
                _listenerLockRecoveryPollingInterval = value;
            }
        }
    }
}
