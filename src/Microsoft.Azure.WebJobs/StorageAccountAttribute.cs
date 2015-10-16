// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Attribute used to override the default Azure Storage account used.
    /// </summary>
    /// <remarks>
    /// This attribute can be applied at the parameter/method/class level, and the precedence
    /// is in that order.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class StorageAccountAttribute : Attribute
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="account">The name of the Azure Storage connection string to use. This should be the name
        /// of one of the connection strings present in the application settings (or environment variables). The
        /// connection string name in settings should be prefixed with the standard "AzureWebJobs" prefix, but the
        /// value you specify here should not include that prefix.
        /// prefix.</param>
        public StorageAccountAttribute(string account)
        {
            Account = account;
        }

        /// <summary>
        /// Gets the Azure Storage account name to use.
        /// </summary>
        public string Account { get; private set; }
    }
}
