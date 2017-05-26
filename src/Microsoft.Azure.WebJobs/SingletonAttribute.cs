// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// This attribute can be applied to a job functions to ensure that only a single
    /// instance of the function is executed at any given time (even across host instances).
    /// A blob lease is used behind the scenes to implement the lock.
    /// <remarks>
    /// This attribute can also be used in <see cref="SingletonMode.Listener"/> mode to ensure that
    /// the listener for a triggered function is only running on a single instance. Trigger bindings
    /// can make this implicit by applying the attribute to their IListener implementation.
    /// Functions can override an implicit singleton by applying a singleton to their function with
    /// mode <see cref="SingletonMode.Listener"/>.
    /// </remarks>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class SingletonAttribute : Attribute
    {
        private int? _lockAcquisitionTimeout;

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public SingletonAttribute()
            : this(string.Empty)
        {
        }

        /// <summary>
        /// Constructs a new instance using the specified scope settings.
        /// </summary>
        /// <param name="scopeId">The scope value for the singleton lock. This value can be used to implement
        /// finer grained locks by including binding parameters that bind to function input data.</param>
        /// <param name="scope">The <see cref="SingletonScope"/> to use. When set to <see cref="SingletonScope.Function"/>, the lock
        /// is only scoped to the specific function, meaning invocations of that function will be serialized. To implement locking across
        /// multiple functions, you can use <see cref="SingletonScope.Host"/> and apply the same attribute to multiple functions.
        /// Each function will then use the same lock, and invocations of all the functions will be serialized.
        /// The default value is <see cref="SingletonScope.Function"/>.
        /// </param>
        public SingletonAttribute(string scopeId, SingletonScope scope = SingletonScope.Function)
        {
            ScopeId = scopeId;
            Mode = SingletonMode.Function;
            Scope = scope;
        }

        /// <summary>
        /// Gets the scope identifier for the singleton lock.
        /// </summary>
        public string ScopeId
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets or sets the <see cref="SingletonScope"/> this singleton should use.
        /// Defaults to <see cref="SingletonScope.Function"/> if not explicitly specified.
        /// </summary>
        public SingletonScope Scope
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets or sets the <see cref="SingletonMode"/> this singleton should use.
        /// Defaults to <see cref="SingletonMode.Function"/> if not explicitly specified.
        /// </summary>
        public SingletonMode Mode { get; set; }

        /// <summary>
        /// Gets the name of the Azure Storage account that the blob lease should be
        /// created in.
        /// </summary>
        /// <remarks>
        /// If not specified, the default AzureWebJobs storage account will be used.
        /// </remarks>
        public string Account { get; set; }

        /// <summary>
        /// Gets or sets the timeout value in seconds for lock acquisition.
        /// If the lock is not obtained within this interval, the invocation will fail.
        /// When set, this value will override the corresponding global configuration
        /// value set in JobHostConfiguration.Singleton.
        /// </summary>
        public int? LockAcquisitionTimeout 
        { 
            get
            {
                return _lockAcquisitionTimeout;
            }

            set
            {
                if (value != null && value <= 0)
                {
                    throw new ArgumentOutOfRangeException("value");
                }
                _lockAcquisitionTimeout = value;
            }
        } 
    }
}
