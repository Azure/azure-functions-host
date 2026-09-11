// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Requests a current snapshot when the revision is omitted or null, or waits for a newer revision when supplied.
/// </summary>
internal sealed class InstanceStatePollRequest
{
    public long? LastKnownRevision { get; init; }
}
