// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Defines an asynchronous collector (an insert-only collection).
    /// </summary>
    /// <typeparam name="T">The type of items to collect.</typeparam>
    public interface IAsyncCollector<in T>
    {
        /// <summary>
        /// Adds an item to the <see cref="IAsyncCollector{T}"/>.
        /// </summary>
        /// <param name="item">The item to be added.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A task that will add the item to the collector.</returns>
        Task AddAsync(T item, CancellationToken cancellationToken = default(CancellationToken));
    }
}
