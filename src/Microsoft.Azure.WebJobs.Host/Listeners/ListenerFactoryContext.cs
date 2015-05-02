// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    /// <summary>
    /// Context object used passed to <see cref="IListenerFactory"/> instances.
    /// </summary>
    public class ListenerFactoryContext
    {
        private readonly CancellationToken _cancellationToken;

        internal ListenerFactoryContext(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Gets the <see cref="CancellationToken"/> to use.
        /// </summary>
        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }
    }
}
