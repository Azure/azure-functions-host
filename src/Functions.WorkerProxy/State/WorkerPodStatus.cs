// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Describes the worker-pod status exposed to the platform.
/// </summary>
internal enum WorkerPodStatus
{
    None,
    ReadyForRequest
}
