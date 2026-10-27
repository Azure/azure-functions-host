// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;

/// <summary>
/// Configures opt-in reverse HTTP transport limits; each process independently owns its aggregate byte budget.
/// </summary>
internal class HttpProxyOptions
{
    public const string HostSectionName = "Functions:IdentityProxy";

    public bool Enabled { get; set; }

    public int MaxRequestBodyBytes { get; set; } = 1024 * 1024;

    public int MaxResponseBodyBytes { get; set; } = 1024 * 1024;

    public int MaxHeaderBytes { get; set; } = 32 * 1024;

    public int MaxHeaderCount { get; set; } = 100;

    public int MaxTargetBytes { get; set; } = 8192;

    public int MaxPendingRequests { get; set; } = 32;

    /// <summary>
    /// Gets or sets reserved buffer capacity per Host transport, or across the local WorkerProxy.
    /// </summary>
    public long MaxBufferedBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the Host-wide request limit shared by all connected WorkerProxy transports.
    /// </summary>
    public int MaxHostRequests { get; set; } = 256;

    /// <summary>
    /// Gets or sets reserved buffer capacity shared by all Host transports.
    /// </summary>
    public long MaxHostBufferedBytes { get; set; } = 256 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the total HTTP request budget, including connection/preparation waits and local response delivery.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the Host registry's independent deadline for an opt-in link attempt before customer startup.
    /// </summary>
    public TimeSpan LinkTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets conservative per-request accounting for bodies, metadata, and serialization copies, not total process memory.
    /// </summary>
    internal long ReservationBytes => (3L * (MaxRequestBodyBytes + (long)MaxResponseBodyBytes))
        + (4L * MaxHeaderBytes) + MaxTargetBytes;

    /// <summary>
    /// Advertises message bounds and outstanding request IDs; aggregate byte budgets remain local to each process.
    /// </summary>
    internal HttpProxyLimits ToLimits() => new()
    {
        MaxRequestBodyBytes = (uint)MaxRequestBodyBytes,
        MaxResponseBodyBytes = (uint)MaxResponseBodyBytes,
        MaxHeaderBytes = (uint)MaxHeaderBytes,
        MaxHeaderCount = (uint)MaxHeaderCount,
        MaxTargetBytes = (uint)MaxTargetBytes,
        MaxPendingRequests = (uint)MaxPendingRequests
    };

    /// <summary>
    /// Uses the stricter local or peer value for each wire limit without increasing either process's buffer budget.
    /// </summary>
    internal HttpProxyLimits Negotiate(HttpProxyLimits peer)
    {
        if (peer is null || peer.MaxRequestBodyBytes == 0 || peer.MaxResponseBodyBytes == 0
            || peer.MaxHeaderBytes == 0 || peer.MaxHeaderCount == 0 || peer.MaxTargetBytes == 0 || peer.MaxPendingRequests == 0)
        {
            throw new HttpProxyException(HttpProxyFailure.Types.Code.InvalidRequest);
        }

        return new()
        {
            MaxRequestBodyBytes = Math.Min((uint)MaxRequestBodyBytes, peer.MaxRequestBodyBytes),
            MaxResponseBodyBytes = Math.Min((uint)MaxResponseBodyBytes, peer.MaxResponseBodyBytes),
            MaxHeaderBytes = Math.Min((uint)MaxHeaderBytes, peer.MaxHeaderBytes),
            MaxHeaderCount = Math.Min((uint)MaxHeaderCount, peer.MaxHeaderCount),
            MaxTargetBytes = Math.Min((uint)MaxTargetBytes, peer.MaxTargetBytes),
            MaxPendingRequests = Math.Min((uint)MaxPendingRequests, peer.MaxPendingRequests)
        };
    }
}
