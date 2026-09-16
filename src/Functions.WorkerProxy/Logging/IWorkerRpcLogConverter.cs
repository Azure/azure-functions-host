// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.WorkerProxy.Logging;

internal interface IWorkerRpcLogConverter
{
    WorkerRpcLogConversionResult Convert(RpcLog rpcLog);
}