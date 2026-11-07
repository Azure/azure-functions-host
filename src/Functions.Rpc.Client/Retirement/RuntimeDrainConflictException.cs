// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Reports an incompatible retry of an irreversible Runtime drain.
/// </summary>
public sealed class RuntimeDrainConflictException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RuntimeDrainConflictException"/> class.
    /// </summary>
    /// <param name="message">The conflict description.</param>
    public RuntimeDrainConflictException(string message)
        : base(message)
    {
    }
}
