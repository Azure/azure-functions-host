// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;
using static Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal partial class GrpcWorkerChannel
    {
        // EventId range is 800-899
        private static class Logger
        {
            private static readonly Action<ILogger, string, ContentOneofCase, Exception> _channelReceivedMessage = LoggerMessage.Define<string, ContentOneofCase>(
                LogLevel.Debug,
                new EventId(820, nameof(ChannelReceivedMessage)),
                "[channel] received {workerId}: {msgType}");

            private static readonly Action<ILogger, string, Exception> _invocationResponseReceived = LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(821, nameof(InvocationResponseReceived)),
                "InvocationResponse received for invocation: '{invocationId}'");

            private static readonly Action<ILogger, string, Exception> _ignoringRpcLog = LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(822, nameof(IgnoringRpcLog)),
                "Ignoring RpcLog from invocation '{invocationId}' because there is no matching ScriptInvocationContext.");

            internal static void ChannelReceivedMessage(ILogger logger, string workerId, ContentOneofCase msgType) => _channelReceivedMessage(logger, workerId, msgType, null);

            internal static void InvocationResponseReceived(ILogger logger, string invocationId) => _invocationResponseReceived(logger, invocationId, null);

            internal static void IgnoringRpcLog(ILogger logger, string invocationId) => _ignoringRpcLog(logger, invocationId, null);
        }
    }
}
