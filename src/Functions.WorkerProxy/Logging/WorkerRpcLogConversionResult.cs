// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Logging;

internal abstract record WorkerRpcLogConversionResult;

internal sealed record WorkerUserLog(
    string InvocationId,
    string LoggerCategory,
    RpcLog.Types.RpcLogCategory RpcLogCategory,
    LogLevel Level,
    string Message,
    EventId EventId,
    WorkerLogException? Exception) : WorkerRpcLogConversionResult;

internal sealed record WorkerSystemLog(
    string LoggerCategory,
    RpcLog.Types.RpcLogCategory RpcLogCategory,
    LogLevel Level,
    string Message,
    EventId EventId,
    WorkerLogException? Exception) : WorkerRpcLogConversionResult;

internal sealed record WorkerCustomMetric(
    string InvocationId,
    string Name,
    double Value,
    IReadOnlyDictionary<string, object?> Properties) : WorkerRpcLogConversionResult;

internal sealed record WorkerCustomMetricConversionFailure(
    string InvocationId,
    WorkerCustomMetricConversionFailureReason Reason) : WorkerRpcLogConversionResult;

internal sealed record WorkerLogException(
    string Source,
    string StackTrace,
    string Message,
    bool IsUserException,
    string Type);