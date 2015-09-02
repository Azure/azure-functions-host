// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Class providing context information for calls to <see cref="StorageClientFactory"/>.
    /// </summary>
    public class StorageClientFactoryContext
    {
        /// <summary>
        /// Gets or sets the <see cref="CloudStorageAccount"/> to create a client for.
        /// </summary>
        [CLSCompliant(false)]
        public CloudStorageAccount Account { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="ParameterInfo"/> for the parameter
        /// binding to create a client for, if the client is being created
        /// for a binding. May be null.
        /// </summary>
        public ParameterInfo Parameter { get; set; }
    }
}
