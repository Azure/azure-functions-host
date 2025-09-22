// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal sealed class WorkerRuntimeResolver : IWorkerRuntimeResolver
    {
        private readonly IEnvironment _environment;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptionsMonitor;
        private readonly IOptionsMonitor<ScriptJobHostOptions> _scriptJobHostOptionsMonitor;
        private string _resolvedWorkerRuntime;

        public WorkerRuntimeResolver(IEnvironment environment,
                                     IOptionsMonitor<StandbyOptions> standbyOptionsMonitor,
                                     IOptionsMonitor<ScriptJobHostOptions> scriptJobHostOptionsMonitor)
        {
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(standbyOptionsMonitor);
            ArgumentNullException.ThrowIfNull(scriptJobHostOptionsMonitor);

            _environment = environment;
            _standbyOptionsMonitor = standbyOptionsMonitor;
            _scriptJobHostOptionsMonitor = scriptJobHostOptionsMonitor;

            if (!_environment.IsCoreTools())
            {
                InitializeStandbyNotification(_standbyOptionsMonitor);
            }
        }

        private void InitializeStandbyNotification(IOptionsMonitor<StandbyOptions> standbyOptionsMonitor)
        {
            if (_standbyOptionsMonitor.CurrentValue.InStandbyMode)
            {
                _standbyOptionsMonitor.OnChange(standbyOptions =>
                {
                    _resolvedWorkerRuntime = null;
                });
            }
        }

        public string GetWorkerRuntime(string defaultValue = null)
        {
            if (_resolvedWorkerRuntime is not null)
            {
                return _resolvedWorkerRuntime;
            }

            if (_environment.IsFlexConsumptionSku()
                && string.Equals(_scriptJobHostOptionsMonitor.CurrentValue.ConfigurationProfile, "mcp-custom-handler"))
            {
                return _resolvedWorkerRuntime = "custom";
            }

            return _resolvedWorkerRuntime = _environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.FunctionWorkerRuntime, defaultValue);
        }
    }
}
