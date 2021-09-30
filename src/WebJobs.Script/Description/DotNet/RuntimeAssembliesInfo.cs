// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class RuntimeAssembliesInfo
    {
        private readonly IEnvironment _environment;
        private Lazy<Dictionary<string, ScriptRuntimeAssembly>> _runtimeAssemblies;
        private object _loadSyncRoot = new object();
        private bool? _relaxedUnification;

        public RuntimeAssembliesInfo()
            : this(SystemEnvironment.Instance)
        {
        }

        public RuntimeAssembliesInfo(IEnvironment instance)
        {
            _environment = instance;
            _runtimeAssemblies = new Lazy<Dictionary<string, ScriptRuntimeAssembly>>(GetRuntimeAssemblies);
        }

        public event EventHandler<EventArgs> Reset;

        public Dictionary<string, ScriptRuntimeAssembly> Assemblies => _runtimeAssemblies.Value;

        private Dictionary<string, ScriptRuntimeAssembly> GetRuntimeAssemblies()
        {
            lock (_loadSyncRoot)
            {
                _relaxedUnification = FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagRelaxedAssemblyUnification, _environment);

                string manifestName = _relaxedUnification.Value
                    ? "runtimeassemblies-relaxed.json"
                    : "runtimeassemblies.json";

                return DependencyHelper.GetRuntimeAssemblies(manifestName);
            }
        }

        public bool ResetIfStale()
        {
            lock (_loadSyncRoot)
            {
                if (_relaxedUnification != null && _relaxedUnification.Value != FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagRelaxedAssemblyUnification, _environment))
                {
                    _runtimeAssemblies = new Lazy<Dictionary<string, ScriptRuntimeAssembly>>(GetRuntimeAssemblies);

                    OnReset();

                    return true;
                }
            }

            return false;
        }

        private void OnReset()
        {
            Reset?.Invoke(this, EventArgs.Empty);
        }
    }
}
