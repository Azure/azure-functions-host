// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Dispatches function invocations through client-backed worker channels.
/// </summary>
public interface IRpcClientFunctionInvocationDispatcher : IFunctionInvocationDispatcher
{
}
