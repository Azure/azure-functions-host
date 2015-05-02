// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    /// <summary>
    /// Interface defining methods used to create <see cref="IListener"/>s for
    /// trigger parameter bindings. See <see cref="Microsoft.Azure.WebJobs.Host.Triggers.ITriggerBinding.CreateListenerFactory"/>
    /// for more information.
    /// </summary>
    public interface IListenerFactory
    {
        /// <summary>
        /// Creates a listener.
        /// </summary>
        /// <param name="context">The <see cref="ListenerFactoryContext"/>.</param>
        /// <returns>The listener.</returns>
        Task<IListener> CreateAsync(ListenerFactoryContext context);
    }
}
