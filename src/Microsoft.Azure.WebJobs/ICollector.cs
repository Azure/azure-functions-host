// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Defines a collector (an insert-only collection).
    /// </summary>
    /// <typeparam name="T">The type of items to collect.</typeparam>
    public interface ICollector<in T>
    {
        /// <summary>
        /// Adds an item to the <see cref="ICollector{T}"/>.
        /// </summary>
        /// <param name="item">The item to be added.</param>
        void Add(T item);
    }
}
