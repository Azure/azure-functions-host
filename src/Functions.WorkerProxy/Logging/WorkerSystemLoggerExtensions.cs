// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;
using WorkerRpcException = Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException;

namespace Azure.Functions.WorkerProxy.Logging;

internal static class WorkerSystemLoggerExtensions
{
    public static void LogWorkerEvent(this ILogger logger, RpcLog workerLog)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(workerLog);

        IDisposable? scope = string.IsNullOrEmpty(workerLog.InvocationId)
            ? null
            : logger.BeginScope(
                new Dictionary<string, object?>
                {
                    [WorkerSystemLoggerProvider.InvocationIdScopeKey] = workerLog.InvocationId
                });

        using (scope)
        {
            LogLevel level = workerLog.Level switch
            {
                RpcLog.Types.Level.Trace => LogLevel.Trace,
                RpcLog.Types.Level.Debug => LogLevel.Debug,
                RpcLog.Types.Level.Information => LogLevel.Information,
                RpcLog.Types.Level.Warning => LogLevel.Warning,
                RpcLog.Types.Level.Error => LogLevel.Error,
                RpcLog.Types.Level.Critical => LogLevel.Critical,
                _ => LogLevel.None
            };
            if (level == LogLevel.None)
            {
                return;
            }

            Exception? exception = level is LogLevel.Error or LogLevel.Critical
                && workerLog.Exception is { } remoteException
                    ? new WorkerRpcException(
                        workerLog.Message,
                        remoteException.Message,
                        remoteException.StackTrace)
                    : null;

            logger.Log(
                level,
                new EventId(0, workerLog.EventId),
                workerLog.Message,
                exception,
                static (message, _) => message);
        }
    }
}
