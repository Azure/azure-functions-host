// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class RuntimeAssembliesInfo
    {
        private readonly IEnvironment _environment;
        private Lazy<Dictionary<string, ScriptRuntimeAssembly>> _runtimeAssemblies;
        private object _loadSyncRoot = new object();
        private bool? _compatMode;

        public RuntimeAssembliesInfo()
            : this(SystemEnvironment.Instance)
        {
        }

        public RuntimeAssembliesInfo(IEnvironment instance)
        {
            _environment = instance;
            _runtimeAssemblies = new Lazy<Dictionary<string, ScriptRuntimeAssembly>>(GetRuntimeAssemblies);
        }

        public Dictionary<string, ScriptRuntimeAssembly> Assemblies => _runtimeAssemblies.Value;

        private Dictionary<string, ScriptRuntimeAssembly> GetRuntimeAssemblies()
        {
            lock (_loadSyncRoot)
            {
                _compatMode = _environment.IsV2CompatibilityMode();

                string manifestName = _compatMode.Value
                    ? "runtimeassemblies.json"
                    : "runtimeassemblies-v3.json";

                return DependencyHelper.GetRuntimeAssemblies(manifestName);
            }
        }

        public bool ResetIfStale()
        {
            lock (_loadSyncRoot)
            {
                if (_compatMode != null && _compatMode.Value != _environment.IsV2CompatibilityMode())
                {
                    _runtimeAssemblies = new Lazy<Dictionary<string, ScriptRuntimeAssembly>>(GetRuntimeAssemblies);

                    return true;
                }
            }

            return false;
        }
    }
}
