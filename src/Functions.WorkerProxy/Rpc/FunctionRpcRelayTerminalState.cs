// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy.Rpc;

/// <summary>
/// Describes the first terminal condition observed by a relay session.
/// </summary>
/// <param name="SessionId">The relay session identifier.</param>
/// <param name="Reason">The terminal condition category.</param>
/// <param name="Side">
/// The side that observed the terminal condition, or <see langword="null"/> for application shutdown.
/// </param>
/// <param name="Exception">The originating exception, when the terminal condition was cancellation or a stream failure.</param>
internal sealed record FunctionRpcRelayTerminalState(long SessionId, FunctionRpcRelayTerminationReason Reason,
    FunctionRpcRelaySide? Side, Exception? Exception);
