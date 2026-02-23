// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection
{
    /// <summary>
    /// WebHost-level adapter for IWorkerRuntimeResolver.
    /// If a Script Host scoped resolver is available, delegates to it.
    /// Otherwise, falls back to environment based resolution.
    /// </summary>
    internal sealed class WebHostWorkerRuntimeResolverAdapter : IWorkerRuntimeResolver, IDisposable
    {
        // Sentinel used to distinguish "not yet resolved" (null) from "resolved to empty/missing".
        // Uses new string instance (not interned) so ReferenceEquals can reliably identify it.
        private static readonly string EnvironmentValueNotSet = new(' ', 0);

        private readonly IServiceProvider _rootProvider;
        private readonly ILogger<WebHostWorkerRuntimeResolverAdapter> _logger;
        private readonly IEnvironment _environment;
        private IWorkerRuntimeResolver _cachedHostResolver;
        private IScriptHostManager _hostManager;
        private string _cachedEnvironmentValue;
        private int _disposed; // 0 = false, 1 = true

        public WebHostWorkerRuntimeResolverAdapter(
            IServiceProvider rootProvider,
            IEnvironment environment,
            ILogger<WebHostWorkerRuntimeResolverAdapter> logger)
        {
            ArgumentNullException.ThrowIfNull(rootProvider);
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(logger);
            _rootProvider = rootProvider;
            _environment = environment;
            _logger = logger;
        }

        public string GetWorkerRuntime(string defaultValue = null)
        {
            EnsureSubscribedToHostManagerStateChange();

            var scriptHostResolver = _cachedHostResolver;

            if (scriptHostResolver is null)
            {
                var scriptHostWorkerResolver = _rootProvider.GetScriptHostServiceOrNull<IWorkerRuntimeResolver>();
                if (scriptHostWorkerResolver is not null)
                {
                    _logger.ScriptHostWorkerResolverResolvedSuccessfully();

                    var existing = Interlocked.CompareExchange(
                        ref _cachedHostResolver,
                        scriptHostWorkerResolver,
                        comparand: null);

                    scriptHostResolver = existing ?? scriptHostWorkerResolver;

                    if (existing is null)
                    {
                        _logger.ScriptHostWorkerResolverCached();
                    }
                }
            }

            if (scriptHostResolver is not null)
            {
                return scriptHostResolver.GetWorkerRuntime(defaultValue);
            }

            // Fallback to environment when Job Host scoped resolver is not available yet
            var cachedValue = _cachedEnvironmentValue;
            if (cachedValue is null)
            {
                var valueFromEnvironment = _environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);

                if (!string.IsNullOrEmpty(valueFromEnvironment))
                {
                    var existing = Interlocked.CompareExchange(ref _cachedEnvironmentValue, valueFromEnvironment, comparand: null);
                    return existing ?? valueFromEnvironment;
                }

                // Cache the "not set" result so we don't re-read the environment on every call.
                Interlocked.CompareExchange(ref _cachedEnvironmentValue, EnvironmentValueNotSet, comparand: null);

                return defaultValue;
            }

            if (ReferenceEquals(cachedValue, EnvironmentValueNotSet))
            {
                return defaultValue;
            }

            return cachedValue;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
            {
                return;
            }

            var hostManager = Interlocked.Exchange(ref _hostManager, null);
            if (hostManager is not null)
            {
                hostManager.ActiveHostChanged -= OnActiveHostChanged;
            }

            _cachedHostResolver = null;
            _cachedEnvironmentValue = null;
        }

        private void EnsureSubscribedToHostManagerStateChange()
        {
            // Fast-path: already subscribed.
            var hostManager = _hostManager;
            if (hostManager is not null)
            {
                return;
            }

            hostManager = _rootProvider.GetRequiredService<IScriptHostManager>();

            var existing = Interlocked.CompareExchange(ref _hostManager, hostManager, null);
            if (existing is null)
            {
                // CompareExchange succeeded. This thread established the initial host manager
                // reference and must attach the event handler as the sole subscriber.
                hostManager.ActiveHostChanged += OnActiveHostChanged;
                _logger.SubscribedToActiveHostChangedEvent();
            }
        }

        private void OnActiveHostChanged(object sender, ActiveHostChangedEventArgs e)
        {
            // Clear cached resolver and environment value when active host changes (host restart/rebuild).
            // The environment value may have changed during specialization.
            Interlocked.Exchange(ref _cachedHostResolver, null);
            Interlocked.Exchange(ref _cachedEnvironmentValue, null);
            _logger.ActiveHostChangedResolverCleared();
        }
    }
}
