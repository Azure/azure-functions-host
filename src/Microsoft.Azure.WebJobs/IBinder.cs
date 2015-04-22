// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>Represents an attribute binder.</summary>
    /// <remarks>This interface enables imperative binding with attribute information specified at runtime.</remarks>
    public interface IBinder
    {
        /// <summary>Binds the specified attribute.</summary>
        /// <typeparam name="T">The type to which to bind.</typeparam>
        /// <param name="attribute">The attribute to bind.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will provide the bound the value.</returns>
        Task<T> BindAsync<T>(Attribute attribute, CancellationToken cancellationToken = default(CancellationToken));
    }
}
