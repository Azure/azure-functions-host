// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    /// <summary>
    /// Interface defining the contract for executing a triggered function.
    /// </summary>
    /// <typeparam name="TTriggerValue">The trigger value type for the trigger binding.</typeparam>
    public interface ITriggeredFunctionExecutor<TTriggerValue> : ITriggeredFunctionExecutor
    {
        /// <summary>
        /// Try to invoke the triggered function using the values specified.
        /// </summary>
        /// <param name="input">The trigger invocation details.</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>True if the invocation succeeded, false otherwise.</returns>
        Task<bool> TryExecuteAsync(TriggeredFunctionData<TTriggerValue> input, CancellationToken cancellationToken);
    }
}
