// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

#if PUBLICSTORAGE
namespace Microsoft.Azure.Jobs.Storage.Table
#else
namespace Microsoft.Azure.Jobs.Host.Storage.Table
#endif
{
    /// <summary>Defines a table client.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface ICloudTableClient
#else
    internal interface ICloudTableClient
#endif
    {
        /// <summary>Gets a table reference.</summary>
        /// <param name="tableName">The table name.</param>
        /// <returns>A table reference.</returns>
        ICloudTable GetTableReference(string tableName);
    }
}
