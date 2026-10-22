// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Root bootstrap options for the compute-only Client worker graph.
/// </summary>
internal sealed class RpcClientOptions
{
    public bool EnableWorkerHostJson { get; set; }
}
