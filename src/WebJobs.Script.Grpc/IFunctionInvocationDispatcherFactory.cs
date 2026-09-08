// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    /// <summary>
    /// Provides the invocation dispatcher selected by the worker topology.
    /// </summary>
    public interface IFunctionInvocationDispatcherFactory
    {
        /// <summary>
        /// Gets the selected invocation dispatcher.
        /// </summary>
        /// <returns>The selected dispatcher.</returns>
        IFunctionInvocationDispatcher GetFunctionDispatcher();
    }
}