// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy;

/// <summary>
/// Identifies the immutable stream side owned by a FunctionRpc endpoint application.
/// </summary>
/// <param name="Side">The side assigned to the endpoint.</param>
internal sealed record FunctionRpcRelayEndpoint(FunctionRpcRelaySide Side);
