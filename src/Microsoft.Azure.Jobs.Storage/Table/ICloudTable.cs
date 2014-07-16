// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.Jobs.Storage.Table
#else
namespace Microsoft.Azure.Jobs.Host.Storage.Table
#endif
{
    /// <summary>Defines a table.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface ICloudTable
#else
    internal interface ICloudTable
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

        /// <summary>Gets an existing entity or inserts a new entity into the table.</summary>
        /// <typeparam name="TElement">The type of the entity.</typeparam>
        /// <param name="entity">The entity to insert, if no existing entity exists.</param>
        /// <returns>The existing entity found on the new entity inserted.</returns>
        TElement GetOrInsert<TElement>(TElement entity) where TElement : ITableEntity, new();
    }
}
