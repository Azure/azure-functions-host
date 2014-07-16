// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.Jobs.Storage.Table
#else
namespace Microsoft.Azure.Jobs.Host.Storage.Table
#endif
{
    /// <summary>Defines a query modifier.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IQueryModifier
#else
    internal interface IQueryModifier
#endif
    {
        /// <summary>Applies a query modifier to a query.</summary>
        /// <typeparam name="T">The type of entities in the query.</typeparam>
        /// <param name="queryable">The queryable to which to apply the modifier.</param>
        /// <returns>The queryable with the modifier applied.</returns>
        IQueryable<T> Apply<T>(IQueryable<T> queryable) where T : ITableEntity;
    }
}
