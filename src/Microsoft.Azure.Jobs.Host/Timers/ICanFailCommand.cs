// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.Jobs.Host.Timers
{
    /// <summary>Defines a command that may fail gracefully.</summary>
    internal interface ICanFailCommand
    {
        /// <summary>Attempts to execute the command.</summary>
        /// <returns><see langword="false"/> if the command fails gracefully; otherwise <see langword="true"/>.</returns>
        /// <remarks>This method returns <see langword="false"/> rather than throwing to indicate a graceful failure.</remarks>
        bool TryExecute();
    }
}
