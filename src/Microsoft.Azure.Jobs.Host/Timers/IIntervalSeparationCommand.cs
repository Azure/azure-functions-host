// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Timers
{
    /// <summary>Defines a command that occurs at an interval that may change with every execution.</summary>
    internal interface IIntervalSeparationCommand
    {
        /// <summary>Returns the current interval to wait before running <see cref="ExecuteAsync"/> again.</summary>
        TimeSpan SeparationInterval { get; }

        /// <summary>Executes the command.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>Calling this method may result in an updated <see cref="SeparationInterval"/>.</remarks>
        /// <returns>A <see cref="Task"/> that will execute the command.</returns>
        Task ExecuteAsync(CancellationToken cancellationToken);
    }
}
