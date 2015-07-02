// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    /// <summary>
    /// Context object used passed to <see cref="ITriggerBinding.CreateListenerAsync"/>.
    /// </summary>
    public class ListenerFactoryContext
    {
        /// <summary>
        /// Creates a new instance
        /// </summary>
        /// <param name="descriptor">The <see cref="FunctionDescriptor"/> to create a listener for.</param>
        /// <param name="executor">The <see cref="ITriggeredFunctionExecutor"/> that should be used to invoke the
        /// target job function when the trigger fires.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use.</param>
        public ListenerFactoryContext(FunctionDescriptor descriptor, ITriggeredFunctionExecutor executor, CancellationToken cancellationToken)
        {
            Descriptor = descriptor;
            Executor = executor;
            CancellationToken = cancellationToken;
        }

        /// <summary>
        /// Gets the <see cref="FunctionDescriptor"/> to create a listener for.
        /// </summary>
        public FunctionDescriptor Descriptor { get; private set; }

        /// <summary>
        /// Gets the <see cref="ITriggeredFunctionExecutor"/> that should be used to invoke the
        /// target job function when the trigger fires.
        /// </summary>
        public ITriggeredFunctionExecutor Executor { get; private set; }

        /// <summary>
        /// Gets the <see cref="CancellationToken"/> to use.
        /// </summary>
        public CancellationToken CancellationToken { get; private set; }
    }
}
