// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using Grpc.Net.Client.Balancer;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// A constant <see cref="IBackoffPolicy"/> that returns the same interval
/// from every call to <see cref="NextBackoff"/>.
/// <para>
/// gRPC's default <c>ExponentialBackoffPolicy</c> is designed for wide-area
/// servers under load, where exponential backoff is necessary to avoid
/// thundering-herd amplification. Compute Separation's runtime-to-worker
/// link is a single host-local pair — there is no upstream service to
/// overwhelm, and the only goal is to react to the worker proxy becoming
/// ready as quickly as possible. A constant policy maximises the retry rate
/// within the readiness budget without ever stretching the wait between
/// retries.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>API stability</b>: <see cref="IBackoffPolicy"/> is marked as an
/// experimental API by <c>Grpc.Net.Client</c> (subject to change without
/// notice). The package version is pinned in <c>Directory.Packages.props</c>;
/// any upgrade should verify this contract is still honoured.
/// </para>
/// </remarks>
internal sealed class ConstantBackoffPolicy : IBackoffPolicy
{
    private readonly TimeSpan _interval;

    public ConstantBackoffPolicy(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Backoff interval must be positive.");
        }

        _interval = interval;
    }

    /// <inheritdoc/>
    public TimeSpan NextBackoff()
    {
        return _interval;
    }
}

/// <summary>
/// A factory that produces <see cref="ConstantBackoffPolicy"/> instances
/// with a fixed interval. Registered against
/// <see cref="GrpcChannelOptions.ServiceProvider"/> so the channel uses it
/// in place of the default <c>ExponentialBackoffPolicyFactory</c>.
/// </summary>
internal sealed class ConstantBackoffPolicyFactory : IBackoffPolicyFactory
{
    private readonly TimeSpan _interval;

    public ConstantBackoffPolicyFactory(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Backoff interval must be positive.");
        }

        _interval = interval;
    }

    /// <inheritdoc/>
    public IBackoffPolicy Create()
    {
        return new ConstantBackoffPolicy(_interval);
    }
}
