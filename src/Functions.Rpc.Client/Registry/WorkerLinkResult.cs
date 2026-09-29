// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Describes a successful link for one caller after initialization and registration complete.
/// </summary>
/// <param name="Channel">The initialized channel, owned by the registry.</param>
/// <param name="IsNewLink">
/// Whether this caller started the successful attempt. Matching retries return <see langword="false"/>
/// even when they waited for the same pending initialization.
/// </param>
public sealed record WorkerLinkResult(WorkerChannel Channel, bool IsNewLink);
