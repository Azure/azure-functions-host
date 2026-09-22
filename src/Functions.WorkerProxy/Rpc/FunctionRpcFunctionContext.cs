// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Rpc;

internal sealed record FunctionRpcFunctionContext(string FunctionId, string FunctionName);