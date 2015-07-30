// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// This attribute can be applied to a job functions to ensure that only a single
    /// instance of the function is executed at any given time (even across host instances).
    /// I.e., invocations are serialized across all host instances (i.e. distributed locking).
    /// This attribute can also be applied to an IListener implementation to ensure that
    /// for functions using that trigger, only a single instance will be listening (for a
    /// particular job function).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class SingletonAttribute : Attribute
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public SingletonAttribute()
            : this(string.Empty)
        {
        }

        /// <summary>
        /// Constructs a new instance using the specified scope.
        /// </summary>
        /// <param name="scope">The scope for the singleton lock. When applied to triggered
        /// job functions, this value can include route parameters.</param>
        public SingletonAttribute(string scope)
        {
            Scope = scope;
        }

        /// <summary>
        /// Gets the scope identifier for the singleton lock.
        /// </summary>
        public string Scope
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets or sets the timeout value for lock acquisition. If the lock is not
        /// obtained within this interval, the invocation will fail.
        /// When set, this value will override the corresponding global configuration
        /// value set in JobHostConfiguration.Singleton.
        /// </summary>
        public TimeSpan? LockAcquisitionTimeout { get; set; } 
    }
}
