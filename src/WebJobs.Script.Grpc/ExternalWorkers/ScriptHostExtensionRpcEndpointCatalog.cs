// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Rpc.Core.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Tracks the extension gRPC endpoints and service-provider lifetime for one ScriptHost generation.
/// </summary>
internal sealed partial class ScriptHostExtensionRpcEndpointCatalog : IHostedService, IDisposable
{
    private readonly Lock _syncLock = new();
    private readonly ExtensionRpcEndpointRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly EndpointDataSource[] _dataSources;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _callCancellationTokenSource = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Dictionary<string, RequestDelegate> _endpoints = new(StringComparer.Ordinal);

    private IDisposable[] _changeTokenRegistrations = [];
    private int _activeCalls;
    private bool _started;
    private bool _draining;
    private bool _disposed;

    /// <summary>
    /// Initializes a catalog for the current ScriptHost endpoint data sources.
    /// </summary>
    /// <param name="registry">The WebHost-level endpoint registry.</param>
    /// <param name="services">The ScriptHost service provider.</param>
    /// <param name="dataSources">The WebJobs RPC endpoint data sources.</param>
    /// <param name="logger">The logger used for catalog diagnostics.</param>
    public ScriptHostExtensionRpcEndpointCatalog(
        ExtensionRpcEndpointRegistry registry,
        IServiceProvider services,
        IEnumerable<WebJobsRpcEndpointDataSource> dataSources,
        ILogger<ScriptHostExtensionRpcEndpointCatalog> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dataSources = dataSources?.Cast<EndpointDataSource>().ToArray()
            ?? throw new ArgumentNullException(nameof(dataSources));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets a value indicating whether this catalog has stopped accepting new calls.
    /// </summary>
    public bool IsDraining
    {
        get
        {
            lock (_syncLock)
            {
                return _draining;
            }
        }
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return Task.CompletedTask;
            }

            RefreshEndpointsUnsynchronized();
            _changeTokenRegistrations = [.. _dataSources.Select(
                dataSource => ChangeToken.OnChange(dataSource.GetChangeToken, RefreshEndpoints))];
            _started = true;
        }

        _registry.Register(this);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _registry.BeginDrain(this);
        await _callCancellationTokenSource.CancelAsync();
        await _drained.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        IDisposable[] registrations;
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            BeginDrainUnsynchronized();
            registrations = _changeTokenRegistrations;
            _changeTokenRegistrations = [];
        }

        _registry.Unregister(this);
        _callCancellationTokenSource.Cancel();
        foreach (IDisposable registration in registrations)
        {
            registration.Dispose();
        }

        _callCancellationTokenSource.Dispose();
    }

    /// <summary>
    /// Acquires a lease for the endpoint registered to the specified gRPC method.
    /// </summary>
    /// <param name="method">The fully qualified gRPC method path.</param>
    /// <returns>The leased endpoint, or <see langword="null"/> when unavailable.</returns>
    public ExtensionRpcEndpoint? TryAcquire(string method)
    {
        lock (_syncLock)
        {
            if (_draining || !_endpoints.TryGetValue(method, out RequestDelegate? requestDelegate))
            {
                return null;
            }

            _activeCalls++;
            return new ExtensionRpcEndpoint(
                requestDelegate, _services, _callCancellationTokenSource.Token, ReleaseAsync);
        }
    }

    /// <summary>
    /// Stops new endpoint acquisition and completes draining after active leases are released.
    /// </summary>
    public void BeginDrain()
    {
        lock (_syncLock)
        {
            BeginDrainUnsynchronized();
        }
    }

    private ValueTask ReleaseAsync()
    {
        lock (_syncLock)
        {
            _activeCalls--;
            if (_draining && _activeCalls is 0)
            {
                _drained.TrySetResult();
            }
        }

        return ValueTask.CompletedTask;
    }

    private void BeginDrainUnsynchronized()
    {
        _draining = true;
        if (_activeCalls is 0)
        {
            _drained.TrySetResult();
        }
    }

    private void RefreshEndpoints()
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            RefreshEndpointsUnsynchronized();
        }
    }

    private void RefreshEndpointsUnsynchronized()
    {
        var endpoints = new Dictionary<string, RequestDelegate>(StringComparer.Ordinal);
        foreach (RouteEndpoint endpoint in _dataSources.SelectMany(p => p.Endpoints).OfType<RouteEndpoint>())
        {
            string? method = endpoint.RoutePattern.RawText;
            if (method is null || endpoint.RequestDelegate is null)
            {
                continue;
            }

            if (!endpoints.TryAdd(method, endpoint.RequestDelegate))
            {
                Log.DuplicateEndpoint(_logger, method);
            }
        }

        _endpoints = endpoints;
    }

    private static partial class Log
    {
        [LoggerMessage(
            LogLevel.Warning,
            "Multiple extension gRPC endpoints registered for method {Method}. The first endpoint will be used.")]
        public static partial void DuplicateEndpoint(ILogger logger, string method);
    }
}
