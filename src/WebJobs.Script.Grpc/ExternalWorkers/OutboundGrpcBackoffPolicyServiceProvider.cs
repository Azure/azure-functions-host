// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using Grpc.Net.Client.Balancer;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Supplies the constant reconnect backoff policy used by outbound gRPC channels.
/// </summary>
internal sealed class OutboundGrpcBackoffPolicyServiceProvider : IServiceProvider
{
    public static readonly OutboundGrpcBackoffPolicyServiceProvider Instance = new();

    private static readonly IBackoffPolicyFactory _factory =
        new ConstantBackoffPolicyFactory(OutboundGrpcClientBase.DefaultRetryInterval);

    private OutboundGrpcBackoffPolicyServiceProvider()
    {
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType)
    {
        return serviceType == typeof(IBackoffPolicyFactory) ? _factory : null;
    }
}
