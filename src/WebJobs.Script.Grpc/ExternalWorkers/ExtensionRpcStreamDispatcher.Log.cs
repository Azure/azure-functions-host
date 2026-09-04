// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

internal sealed partial class ExtensionRpcStreamDispatcher
{
    private static partial class Log
    {
        [LoggerMessage(LogLevel.Debug, "Extension RPC call stopped after its stream closed.")]
        public static partial void CallStoppedAfterStreamClosed(ILogger logger, Exception exception);

        [LoggerMessage(
            LogLevel.Error,
            "Extension gRPC dispatch failed for method {Method} and worker {WorkerId}.")]
        public static partial void DispatchFailed(
            ILogger logger,
            Exception exception,
            string method,
            string workerId);

        [LoggerMessage(LogLevel.Error, "Extension RPC endpoint lease release failed for call {CallId}.")]
        public static partial void EndpointLeaseReleaseFailed(ILogger logger, Exception exception, string callId);

        [LoggerMessage(LogLevel.Debug, "Extension RPC call task stopped for call {CallId}.")]
        public static partial void CallTaskStopped(ILogger logger, Exception exception, string callId);

        [LoggerMessage(
            LogLevel.Debug,
            "Extension RPC stream closed before call {CallId} could send terminal status {Status}.")]
        public static partial void StreamClosedBeforeTerminalStatus(
            ILogger logger,
            string callId,
            ExtensionRpcStatus status);

        [LoggerMessage(
            LogLevel.Debug,
            "Extension RPC terminal status {Status} for call {CallId} was dropped because the outbound queue "
            + "remained blocked.")]
        public static partial void TerminalStatusDropped(
            ILogger logger,
            ExtensionRpcStatus status,
            string callId);
    }
}
