// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Rpc.Core.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    /// <summary>
    /// Endpoint data source which composes all the WebJobs extension data sources together.
    /// </summary>
    /// <remarks>
    /// Implementation is adapted from <see cref="CompositeEndpointDataSource"/>.
    /// https://github.com/dotnet/aspnetcore/blob/main/src/Http/Routing/src/CompositeEndpointDataSource.cs.
    /// </remarks>
    internal sealed class ExtensionsCompositeEndpointDataSource : EndpointDataSource, IDisposable
    {
        private readonly object _lock = new();
        private readonly List<EndpointDataSource> _dataSources = new();
        private readonly IScriptHostManager _scriptHostManager;

        private IServiceProvider _extensionServices;
        private List<Endpoint> _endpoints;
        private IChangeToken _consumerChangeToken;
        private CancellationTokenSource _cts;
        private List<IDisposable> _changeTokenRegistrations;
        private bool _disposed;

        public ExtensionsCompositeEndpointDataSource(IScriptHostManager scriptHostManager)
        {
            _scriptHostManager = scriptHostManager;
            _scriptHostManager.ActiveHostChanged += OnHostChanged;
        }

        /// <inheritdoc />
        public override IReadOnlyList<Endpoint> Endpoints
        {
            get
            {
                ThrowIfDisposed();
                EnsureEndpointsInitialized();
                return _endpoints;
            }
        }

        /// <inheritdoc />
        public override IChangeToken GetChangeToken()
        {
            ThrowIfDisposed();
            EnsureChangeTokenInitialized();
            return _consumerChangeToken;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            List<IDisposable> disposables = null;
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _scriptHostManager.ActiveHostChanged -= OnHostChanged;
                _disposed = true;
                if (_changeTokenRegistrations is { Count: > 0 })
                {
                    disposables ??= new List<IDisposable>();
                    disposables.AddRange(_changeTokenRegistrations);
                    _changeTokenRegistrations = null;
                }
            }

            // Dispose everything outside of the lock in case a registration is blocking on HandleChange completing
            // on another thread or something.
            if (disposables is not null)
            {
                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            }

            _cts?.Dispose();
        }

        private Endpoint WrapEndpoint(Endpoint endpoint)
        {
            static RequestDelegate CreateDelegate(RequestDelegate next, IServiceProvider services)
            {
                // Incoming HttpContext has the gRPC script host services. Create a scope
                // for the JobHost services and swap out the contexts request services.
                return async context =>
                {
                    if (next is null)
                    {
                        return;
                    }

                    IServiceProvider original = context.RequestServices;

                    try
                    {
                        await using AsyncServiceScope scope = services.CreateAsyncScope();
                        context.RequestServices = scope.ServiceProvider;
                        await next(context);
                    }
                    finally
                    {
                        context.RequestServices = original;
                    }
                };
            }

            if (endpoint is not RouteEndpoint route)
            {
                // We only wrap URL-routeable endpoints (ie: RouteEndpoint).
                return endpoint;
            }

            IServiceProvider services = _extensionServices
                ?? throw new InvalidOperationException(
                    "Trying to register extension endpoints, but no extension IServiceProvider available.");

            return new RouteEndpoint(
                CreateDelegate(route.RequestDelegate, services),
                route.RoutePattern,
                route.Order,
                route.Metadata,
                route.DisplayName);
        }

        [MemberNotNull(nameof(_consumerChangeToken))]
        private void EnsureChangeTokenInitialized()
        {
            if (_consumerChangeToken is not null)
            {
                return;
            }

            lock (_lock)
            {
                if (_consumerChangeToken is not null)
                {
                    return;
                }

                // This is our first time initializing the change token, so the collection has "changed" from nothing.
                CreateChangeTokenUnsynchronized(collectionChanged: true);
            }
        }

        [MemberNotNull(nameof(_consumerChangeToken))]
        private void CreateChangeTokenUnsynchronized(bool collectionChanged)
        {
            CancellationTokenSource cts = new();

            if (collectionChanged)
            {
                _changeTokenRegistrations = new();
                foreach (var dataSource in _dataSources)
                {
                    _changeTokenRegistrations.Add(ChangeToken.OnChange(
                        dataSource.GetChangeToken,
                        () => OnEndpointsChange(collectionChanged: false)));
                }
            }

            _cts = cts;
            _consumerChangeToken = new CancellationChangeToken(cts.Token);
        }

        private void OnHostChanged(object sender, ActiveHostChangedEventArgs args)
        {
            lock (_lock)
            {
                _dataSources.Clear();
                if (args?.NewHost?.Services is { } services)
                {
                    _extensionServices = services;
                    IEnumerable<WebJobsRpcEndpointDataSource> sources = services
                        .GetService<IEnumerable<WebJobsRpcEndpointDataSource>>()
                        ?? Enumerable.Empty<WebJobsRpcEndpointDataSource>();
                    _dataSources.AddRange(sources);
                }
                else
                {
                    _extensionServices = null;
                }
            }

            OnEndpointsChange(collectionChanged: true);
        }

        private void OnEndpointsChange(bool collectionChanged)
        {
            CancellationTokenSource oldTokenSource = null;
            List<IDisposable> oldChangeTokenRegistrations = null;

            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                // Prevent consumers from re-registering callback to in-flight events as that can
                // cause a stack overflow.
                // Example:
                // 1. B registers A.
                // 2. A fires event causing B's callback to get called.
                // 3. B executes some code in its callback, but needs to re-register callback
                //    in the same callback.
                oldTokenSource = _cts;
                oldChangeTokenRegistrations = _changeTokenRegistrations;

                // Don't create a new change token if no one is listening.
                if (oldTokenSource is not null)
                {
                    // We have to hook to any OnChange callbacks before caching endpoints,
                    // otherwise we might miss changes that occurred to one of the _dataSources after caching.
                    CreateChangeTokenUnsynchronized(collectionChanged);
                }

                // Don't update endpoints if no one has read them yet.
                if (_endpoints is not null)
                {
                    // Refresh the endpoints from data source so that callbacks can get the latest endpoints.
                    CreateEndpointsUnsynchronized();
                }
            }

            // Disposing registrations can block on user defined code on running on other threads that could try to acquire the _lock.
            if (collectionChanged && oldChangeTokenRegistrations is not null)
            {
                foreach (var registration in oldChangeTokenRegistrations)
                {
                    registration.Dispose();
                }
            }

            // Raise consumer callbacks. Any new callback registration would happen on the new token created in earlier step.
            // Avoid raising callbacks inside a lock.
            oldTokenSource?.Cancel();
        }

        [MemberNotNull(nameof(_endpoints))]
        private void CreateEndpointsUnsynchronized()
        {
            var endpoints = new List<Endpoint>();

            foreach (var dataSource in _dataSources)
            {
                endpoints.AddRange(dataSource.Endpoints.Select(WrapEndpoint));
            }

            // Only cache _endpoints after everything succeeds without throwing.
            // We don't want to create a negative cache which would cause 404s when there should be 500s.
            _endpoints = endpoints;
        }

        // Defer initialization to avoid doing lots of reflection on startup.
        [MemberNotNull(nameof(_endpoints))]
        private void EnsureEndpointsInitialized()
        {
            if (_endpoints is not null)
            {
                return;
            }

            lock (_lock)
            {
                if (_endpoints is not null)
                {
                    return;
                }

                // Now that we're caching the _enpoints, we're responsible for keeping them up-to-date even if the caller
                // hasn't started listening for changes themselves yet.
                EnsureChangeTokenInitialized();

                // Note: we can't use DataSourceDependentCache here because we also need to handle a list of change
                // tokens, which is a complication most of our code doesn't have.
                CreateEndpointsUnsynchronized();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ExtensionsCompositeEndpointDataSource));
            }
        }
    }
}
