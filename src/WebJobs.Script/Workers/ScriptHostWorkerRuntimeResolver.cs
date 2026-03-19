// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    /// <summary>
    /// Resolves the worker runtime for the current script host instance.
    /// At this scope, the functions worker runtime is expected to be present;
    /// the hosting infrastructure only reaches this point after the runtime
    /// has been configured (e.g., via environment variables or specialization).
    /// </summary>
    internal sealed class ScriptHostWorkerRuntimeResolver : IWorkerRuntimeResolver
    {
        private readonly IConfiguration _configuration;
        private readonly IOptionsMonitor<ScriptJobHostOptions> _scriptJobHostOptionsMonitor;
        private string _resolvedWorkerRuntime;

        public ScriptHostWorkerRuntimeResolver(
            IConfiguration configuration,
            IOptionsMonitor<ScriptJobHostOptions> scriptJobHostOptionsMonitor)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(scriptJobHostOptionsMonitor);

            _configuration = configuration;
            _scriptJobHostOptionsMonitor = scriptJobHostOptionsMonitor;
        }

        public string GetWorkerRuntime(string defaultValue = null)
        {
            var cachedRuntime = _resolvedWorkerRuntime;
            if (cachedRuntime is not null)
            {
                return cachedRuntime;
            }

            // Check the worker runtime from the options first (set by configuration profiles),
            // then fall back to the configuration entry (e.g., FUNCTIONS_WORKER_RUNTIME env var).
            string workerRuntime = _scriptJobHostOptionsMonitor.CurrentValue.ProfileWorkerRuntime;

            if (string.IsNullOrEmpty(workerRuntime))
            {
                workerRuntime = _configuration[EnvironmentSettingNames.FunctionWorkerRuntime];
            }

            // Only cache non-null resolved values. Default values should not be cached
            // as they may differ across callers.
            if (workerRuntime is not null)
            {
                var existing = Interlocked.CompareExchange(ref _resolvedWorkerRuntime, workerRuntime, comparand: null);
                return existing ?? workerRuntime;
            }

            return defaultValue;
        }
    }
}
