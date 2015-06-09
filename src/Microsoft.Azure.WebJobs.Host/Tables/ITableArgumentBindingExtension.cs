// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    /// <summary>
    /// Defines an interface for Table argument binding extensions.
    /// </summary>
    [CLSCompliant(false)]
    public interface ITableArgumentBindingExtension : IArgumentBinding<CloudTable>
    {
        /// <summary>
        /// Gets the <see cref="FileAccess"/> value indicating what types of storage
        /// operations this binding supports.
        /// </summary>
        FileAccess Access { get; }
    }
}
