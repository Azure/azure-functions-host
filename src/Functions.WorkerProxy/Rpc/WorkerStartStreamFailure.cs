// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Rpc;

/// <summary>
/// Identifies a bounded reason that WorkerProxy rejected a worker StartStream.
/// </summary>
internal enum WorkerStartStreamFailure
{
    InvalidMessage,
    StaleOrRepeated
}
