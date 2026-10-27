// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Describes a successful link for one caller after the WorkerInit handshake and registration complete.
/// </summary>
/// <param name="Channel">The registered channel, owned by the registry; extension preparation may still be in progress.</param>
/// <param name="IsNewLink">
/// Whether this caller started the successful attempt. Matching retries return <see langword="false"/>
/// even when they waited for the same pending handshake.
/// </param>
public sealed record WorkerLinkResult(WorkerChannel Channel, bool IsNewLink);
