// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    /// <summary>
    /// Defines an argument binding for <see cref="IStorageTable"/> arguments.
    /// </summary>
    /// <remarks><see cref="IStorageTable"/> is our own internal abstraction used for testing,
    /// and is not exposed publically.</remarks>
    internal interface IStorageTableArgumentBinding : IArgumentBinding<IStorageTable>
    {
        /// <summary>
        /// Gets the <see cref="FileAccess"/> that defines the storage operations the
        /// binding supports.
        /// </summary>
        FileAccess Access { get; }
    }
}
