// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table.Queryable;

#if PUBLICSTORAGE
namespace Microsoft.Azure.Jobs.Storage.Table
#else
namespace Microsoft.Azure.Jobs.Host.Storage.Table
#endif
{
    /// <summary>Represents a query modifier that adds an entity resolver.</summary>
    /// <typeparam name="TResult">The type of the resolved entities.</typeparam>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class ResolverQueryModifier<TResult> : IQueryModifier
#else
    internal class ResolverQueryModifier<TResult> : IQueryModifier
#endif
    {
        private readonly EntityResolver<TResult> _entityResolver;

        /// <summary>Initializes a new instance of the <see cref="ResolverQueryModifier{TResult}"/> class.</summary>
        /// <param name="entityResolver">The entity resolver to use to resolve entities.</param>
        public ResolverQueryModifier(EntityResolver<TResult> entityResolver)
        {
            _entityResolver = entityResolver;
        }

        /// <inheritdoc />
        public IQueryable<T> Apply<T>(IQueryable<T> q) where T : ITableEntity
        {
            return (IQueryable<T>)(IQueryable<TResult>)q.Resolve(_entityResolver);
        }
    }
}
