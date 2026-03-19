// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
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
        private readonly IServiceProvider _rootProvider;
        private readonly ILogger<WebHostWorkerRuntimeResolverAdapter> _logger;
        private readonly IConfiguration _configuration;
        private IWorkerRuntimeResolver _cachedHostResolver;
        private IScriptHostManager _hostManager;
        private string _cachedEnvironmentValue;
        private int _disposed; // 0 = false, 1 = true

        public WebHostWorkerRuntimeResolverAdapter(
            IServiceProvider rootProvider,
            IConfiguration configuration,
            ILogger<WebHostWorkerRuntimeResolverAdapter> logger)
        {
            ArgumentNullException.ThrowIfNull(rootProvider);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(logger);
            _rootProvider = rootProvider;
            _configuration = configuration;
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

            // Fallback to environment when Job Host scoped resolver is not available yet.
            // Only cache non-empty values. During specialization, the env var may be set
            // after a previous read returned null, so we must re-read on each call until
            // a value is available. Caching the "not set" state would cause a race condition
            // since we rely on OnActiveHostChanged to clear the cache, which fires after
            // the value is already needed.
            var cachedValue = _cachedEnvironmentValue;
            if (cachedValue is not null)
            {
                return cachedValue;
            }

            var valueFromEnvironment = _configuration[EnvironmentSettingNames.FunctionWorkerRuntime];

            if (!string.IsNullOrEmpty(valueFromEnvironment))
            {
                var existing = Interlocked.CompareExchange(ref _cachedEnvironmentValue, valueFromEnvironment, comparand: null);
                return existing ?? valueFromEnvironment;
            }

            return defaultValue;
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
