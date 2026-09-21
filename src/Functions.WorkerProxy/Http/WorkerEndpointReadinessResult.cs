// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Describes the result of waiting for a worker HTTP destination.
/// </summary>
internal enum WorkerEndpointReadinessResult
{
    Ready,
    NameResolutionFailed,
    ConnectionRefused,
    Timeout,
    ConnectionFailed
}
