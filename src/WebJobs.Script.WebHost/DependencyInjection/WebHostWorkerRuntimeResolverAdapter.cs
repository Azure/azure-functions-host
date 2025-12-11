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
        private readonly IServiceProvider _rootProvider;
        private readonly ILogger<WebHostWorkerRuntimeResolverAdapter> _logger;
        private IWorkerRuntimeResolver _cachedHostResolver;
        private IScriptHostManager _hostManager;
        private IEnvironment _environment;
        private int _disposed; // 0 = false, 1 = true

        public WebHostWorkerRuntimeResolverAdapter(
            IServiceProvider rootProvider,
            ILogger<WebHostWorkerRuntimeResolverAdapter> logger)
        {
            ArgumentNullException.ThrowIfNull(rootProvider);
            ArgumentNullException.ThrowIfNull(logger);
            _rootProvider = rootProvider;
            _logger = logger;
        }

        public string GetWorkerRuntime(string defaultValue = null)
        {
            EnsureSubscribedToHostManagerStateChange();

            var jobHostResolver = _cachedHostResolver;

            if (jobHostResolver is null)
            {
                var scriptHostWorkerResolver = _rootProvider.GetScriptHostServiceOrNull<IWorkerRuntimeResolver>();
                if (scriptHostWorkerResolver is not null)
                {
                    _logger.LogDebug("Script host worker resolver resolved successfully.");

                    var existing = Interlocked.CompareExchange(
                        ref _cachedHostResolver,
                        scriptHostWorkerResolver,
                        comparand: null);

                    jobHostResolver = existing ?? scriptHostWorkerResolver;

                    if (existing is null)
                    {
                        _logger.ScriptHostWorkerResolverCached();
                    }
                }
            }

            if (jobHostResolver is not null)
            {
                return jobHostResolver.GetWorkerRuntime(defaultValue);
            }

            // Fallback to read from environment when Job Host scoped resolver is not available yet
            _environment ??= _rootProvider.GetRequiredService<IEnvironment>();

            return _environment.GetEnvironmentVariableOrDefault(
                EnvironmentSettingNames.FunctionWorkerRuntime,
                defaultValue);
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
            _environment = null;
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
            // Clear cached resolver when active host changes (host restart/rebuild)
            Interlocked.Exchange(ref _cachedHostResolver, null);
            _logger.ActiveHostChangedResolverCleared();
        }
    }
}
