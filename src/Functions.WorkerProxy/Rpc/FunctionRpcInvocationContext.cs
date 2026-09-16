// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Rpc;

internal sealed record FunctionRpcInvocationContext(
    string InvocationId,
    string FunctionId,
    string? FunctionName,
    string? TraceParent,
    string? TraceState);