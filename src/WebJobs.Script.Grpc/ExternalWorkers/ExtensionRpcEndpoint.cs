// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Represents an acquired ASP.NET Core extension gRPC endpoint and its service-provider lease.
/// </summary>
internal sealed class ExtensionRpcEndpoint : IAsyncDisposable
{
    private readonly Func<ValueTask>? _release;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionRpcEndpoint"/> class.
    /// </summary>
    /// <param name="requestDelegate">The ASP.NET Core endpoint delegate.</param>
    /// <param name="services">The service provider associated with the endpoint.</param>
    public ExtensionRpcEndpoint(RequestDelegate requestDelegate, IServiceProvider services)
        : this(requestDelegate, services, CancellationToken.None, release: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionRpcEndpoint"/> class.
    /// </summary>
    /// <param name="requestDelegate">The ASP.NET Core endpoint delegate.</param>
    /// <param name="services">The service provider associated with the endpoint.</param>
    /// <param name="cancellationToken">A token that is cancelled when the endpoint host drains.</param>
    /// <param name="release">The callback that releases the endpoint lease.</param>
    internal ExtensionRpcEndpoint(
        RequestDelegate requestDelegate,
        IServiceProvider services,
        CancellationToken cancellationToken,
        Func<ValueTask>? release)
    {
        RequestDelegate = requestDelegate;
        Services = services;
        CancellationToken = cancellationToken;
        _release = release;
    }

    /// <summary>
    /// Gets the ASP.NET Core endpoint delegate.
    /// </summary>
    public RequestDelegate RequestDelegate { get; }

    /// <summary>
    /// Gets the service provider associated with the endpoint registration.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the token that is cancelled when the endpoint host drains.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        // Concurrent callers do not share the first caller's release completion. Each endpoint is a
        // uniquely owned call lease with a single production disposer, so the interlock only needs
        // to prevent an accidental duplicate release; task sharing would add unused complexity.
        return Interlocked.Exchange(ref _disposed, 1) is 0 && _release is not null
            ? _release()
            : ValueTask.CompletedTask;
    }
}
