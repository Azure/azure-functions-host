// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Signals an attachment that its shared relay session has terminated.
/// </summary>
internal sealed class FunctionRpcRelayTerminatedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionRpcRelayTerminatedException"/> class.
    /// </summary>
    /// <param name="terminalState">The first terminal state recorded by the session.</param>
    public FunctionRpcRelayTerminatedException(FunctionRpcRelayTerminalState terminalState)
        : base("The FunctionRpc relay session terminated.", terminalState.Exception)
    {
        TerminalState = terminalState;
    }

    /// <summary>
    /// Gets the first terminal state recorded by the relay session.
    /// </summary>
    public FunctionRpcRelayTerminalState TerminalState { get; }
}
