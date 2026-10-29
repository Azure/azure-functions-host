// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;

/// <summary>
/// Configures opt-in reverse HTTP transport limits; each process independently owns its aggregate byte budget.
/// </summary>
public class TokenServiceProxyOptions
{
    /// <summary>
    /// The Host configuration section for the Token Service proxy.
    /// </summary>
    public const string HostSectionName = "Functions:TokenServiceProxy";

    /// <summary>
    /// Gets or sets a value indicating whether the Token Service proxy is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum buffered request body size in bytes.
    /// </summary>
    public int MaxRequestBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum buffered response body size in bytes.
    /// </summary>
    public int MaxResponseBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum encoded header size per request or response in bytes.
    /// </summary>
    public int MaxHeaderBytes { get; set; } = 32 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of header values per request or response, including repeated values.
    /// </summary>
    public int MaxHeaderCount { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum encoded request-target size in bytes.
    /// </summary>
    public int MaxTargetBytes { get; set; } = 8192;

    /// <summary>
    /// Gets or sets the maximum number of outstanding requests per transport.
    /// </summary>
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
    public long ReservationBytes => (3L * (MaxRequestBodyBytes + (long)MaxResponseBodyBytes))
        + (4L * MaxHeaderBytes) + MaxTargetBytes;

    /// <summary>
    /// Advertises message bounds and outstanding request IDs; aggregate byte budgets remain local to each process.
    /// </summary>
    /// <returns>The message limits advertised to the peer.</returns>
    public HttpProxyLimits ToLimits() => new()
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
    /// <param name="peer">The limits advertised by the peer.</param>
    /// <returns>The minimum local and peer value for each message limit.</returns>
    /// <exception cref="TokenServiceProxyException">The peer omitted limits or advertised a zero limit.</exception>
    public HttpProxyLimits Negotiate(HttpProxyLimits peer)
    {
        if (peer is null || peer.MaxRequestBodyBytes == 0 || peer.MaxResponseBodyBytes == 0
            || peer.MaxHeaderBytes == 0 || peer.MaxHeaderCount == 0 || peer.MaxTargetBytes == 0 || peer.MaxPendingRequests == 0)
        {
            throw new TokenServiceProxyException(HttpProxyFailure.Types.Code.InvalidRequest);
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
