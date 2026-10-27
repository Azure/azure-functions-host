// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Logging;

internal interface IWorkerSystemEventGenerator
{
    void LogWorkerEvent(RpcLog log);

    void LogProxyEvent(LogLevel level, string eventName, string message);
}
