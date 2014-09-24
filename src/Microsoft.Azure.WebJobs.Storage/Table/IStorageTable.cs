// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    /// <summary>Defines a table.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStorageTable
#else
    internal interface IStorageTable
#endif
    {
        /// <summary>Inserts an entity into the table.</summary>
        /// <param name="entity">The entity to insert.</param>
        void Insert(ITableEntity entity);

        /// <summary>Queries the table.</summary>
        /// <typeparam name="TElement">The type of entities to query.</typeparam>
        /// <param name="limit">The maximum number of rows to retrieve, if any.</param>
        /// <param name="queryModifiers">The query modifiers.</param>
        /// <returns>The matching entities.</returns>
        IEnumerable<TElement> Query<TElement>(int? limit, params IQueryModifier[] queryModifiers) where TElement : ITableEntity, new();
    }
}
