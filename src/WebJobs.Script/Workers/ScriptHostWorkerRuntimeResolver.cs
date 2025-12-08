// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    /// <summary>
    /// Resolves the worker runtime for the current script host instance.
    /// </summary>
    internal sealed class ScriptHostWorkerRuntimeResolver : IWorkerRuntimeResolver
    {
        private readonly IEnvironment _environment;
        private readonly IOptionsMonitor<ScriptJobHostOptions> _scriptJobHostOptionsMonitor;
        private string _resolvedWorkerRuntime;

        public ScriptHostWorkerRuntimeResolver(
            IEnvironment environment,
            IOptionsMonitor<ScriptJobHostOptions> scriptJobHostOptionsMonitor)
        {
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(scriptJobHostOptionsMonitor);

            _environment = environment;
            _scriptJobHostOptionsMonitor = scriptJobHostOptionsMonitor;
        }

        public string GetWorkerRuntime(string defaultValue = null)
        {
            if (_resolvedWorkerRuntime is not null)
            {
                return _resolvedWorkerRuntime;
            }

            if (string.Equals(_scriptJobHostOptionsMonitor.CurrentValue.ConfigurationProfile,
                              HostConfigurationProfile.McpCustomHandlerProfile,
                              StringComparison.OrdinalIgnoreCase)
                || string.Equals(_scriptJobHostOptionsMonitor.CurrentValue.ConfigurationProfile,
                                 HostConfigurationProfile.WebAppCustomHandlerProfile,
                                 StringComparison.OrdinalIgnoreCase))
            {
                return _resolvedWorkerRuntime = "custom";
            }

            return _resolvedWorkerRuntime = _environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.FunctionWorkerRuntime, defaultValue);
        }
    }
}
