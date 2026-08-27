// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Channels;

namespace Azure.Functions.Rpc.Client;

internal sealed partial class RpcClientConnection
{
    private enum TerminalKind
    {
        Completed,
        Disposed,
        Faulted,
    }

    /// <summary>
    /// Carries the winning terminal outcome and any failure encountered while releasing connection resources.
    /// </summary>
    private sealed class TerminalState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TerminalState"/> class.
        /// </summary>
        /// <param name="kind">The reason the connection terminated.</param>
        /// <param name="exception">The transport failure, when termination was faulted.</param>
        private TerminalState(TerminalKind kind, Exception exception = null)
        {
            Kind = kind;
            Exception = exception;
        }

        /// <summary>
        /// Gets the reason the connection terminated.
        /// </summary>
        internal TerminalKind Kind { get; }

        /// <summary>
        /// Gets the transport failure that caused termination, if any.
        /// </summary>
        internal Exception Exception { get; }

        /// <summary>
        /// Gets or sets a failure encountered while cancelling or releasing owned resources.
        /// </summary>
        internal Exception CleanupException { get; set; }

        /// <summary>
        /// Creates a clean peer-completion outcome.
        /// </summary>
        /// <returns>A new clean terminal state.</returns>
        internal static TerminalState CreateCompleted() => new(TerminalKind.Completed);

        /// <summary>
        /// Creates an explicit-disposal outcome.
        /// </summary>
        /// <returns>A new disposed terminal state.</returns>
        internal static TerminalState CreateDisposed() => new(TerminalKind.Disposed);

        /// <summary>
        /// Creates a faulted outcome while distinguishing unexpected transport cancellation from caller cancellation.
        /// </summary>
        /// <param name="exception">The failure reported by a message pump.</param>
        /// <returns>A new faulted terminal state.</returns>
        internal static TerminalState Faulted(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            Exception transportException = exception is ChannelClosedException { InnerException: not null } channelClosedException
                ? channelClosedException.InnerException
                : exception;
            Exception terminalException = transportException is OperationCanceledException
                ? new InvalidOperationException("The RPC client transport was canceled unexpectedly.", transportException)
                : transportException;
            return new TerminalState(TerminalKind.Faulted, terminalException);
        }
    }
}
