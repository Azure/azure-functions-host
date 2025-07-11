// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.ObjectPool;

namespace Microsoft.Azure.WebJobs.Script.Pools
{
    internal static class ObjectPoolExtensions
    {
        /// <summary>
        /// Rents a <see cref="T"/> from the pool. The object is returned on disposal
        /// of the <see cref="PoolRental"/>.
        /// </summary>
        /// <typeparam name="T">The object type the pool holds.</typeparam>
        /// <param name="pool">The pool to rent from.</param>
        /// <returns>
        /// A disposable struct to return the object to the pool.
        /// DO NOT dispose multiple times.
        /// </returns>
        public static PoolRental<T> Rent<T>(this ObjectPool<T> pool)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(pool);
            return new PoolRental<T>(pool);
        }
    }
}
