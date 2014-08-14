// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Timers
{
    /// <summary>
    /// Defines a recurring command that may fail gracefully as well as short-circuit the delay between excecutions.
    /// </summary>
    internal interface IAlertingRecurrentCommand
    {
        /// <summary>Attempts to execute the command.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A <see cref="Task"/> that will execute the command.
        /// </returns>
        /// <remarks>
        /// The task completes successfully with a <see langword="false"/> result rather than faulting to indicate a
        /// graceful failure.
        /// The task completes the StopWating task on the result to short-circuit the normal execution delay.
        /// </remarks>
        Task<AlertingRecurrentCommandResult> TryExecuteAsync(CancellationToken cancellationToken);
    }
}
