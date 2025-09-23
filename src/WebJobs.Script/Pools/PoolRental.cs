// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.ObjectPool;

namespace Microsoft.Azure.WebJobs.Script.Pools
{
    /// <summary>
    /// Returns a pool rental back to the object pool on disposal.
    /// IMPORTANT: do not dispose multiple times.
    /// </summary>
    /// <remarks>
    /// This is a ref struct to try and enforce only inlining in a using statement.
    /// </remarks>
    /// <param name="callback">The callback to return the object.</param>
    /// <param name="state">The object to return.</param>
    internal readonly ref struct PoolRental<T>(ObjectPool<T> pool)
        where T : class
    {
        /// <summary>
        /// Gets the value of this rental.
        /// </summary>
        public T Value { get; } = pool.Get();

        /// <summary>
        /// Returns the object back to the pool.
        /// </summary>
        public void Dispose()
        {
            pool.Return(Value);
        }
    }
}
