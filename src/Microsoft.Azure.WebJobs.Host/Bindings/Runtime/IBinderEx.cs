// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Runtime
{
    /// <summary>
    /// Interface defining functionality for dynamically binding to WebJobs SDK attributes
    /// at runtime.
    /// </summary>
    /// <remarks>This interface enables imperative binding with attribute information specified at runtime.</remarks>
    public interface IBinderEx
    {
        /// <summary>
        /// Gets the binding context.
        /// </summary>
        AmbientBindingContext BindingContext { get; }

        /// <summary>
        /// Binds using the specified <see cref="RuntimeBindingContext"/>.
        /// </summary>
        /// <typeparam name="T">The type to bind to.</typeparam>
        /// <param name="context">The binding context.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will provide the bound the value.</returns>
        Task<T> BindAsync<T>(RuntimeBindingContext context, CancellationToken cancellationToken = default(CancellationToken));
    }
}
