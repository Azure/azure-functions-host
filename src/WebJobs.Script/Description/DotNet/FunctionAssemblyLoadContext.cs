// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Establishes an assembly load context for a extensions, functions and their dependencies.
    /// </summary>
    public partial class FunctionAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _baseProbingPath;
        private static readonly Lazy<string[]> _runtimeAssemblies = new Lazy<string[]>(GetRuntimeAssemblies);

        private static Lazy<FunctionAssemblyLoadContext> _defaultContext = new Lazy<FunctionAssemblyLoadContext>(() => new FunctionAssemblyLoadContext(ResolveFunctionAppRoot()), true);

        public FunctionAssemblyLoadContext(string functionAppRoot)
        {
            if (functionAppRoot == null)
            {
                throw new ArgumentNullException(nameof(functionAppRoot));
            }

            _baseProbingPath = Path.Combine(functionAppRoot, "bin");
        }

        public static FunctionAssemblyLoadContext Shared => _defaultContext.Value;

        protected virtual Assembly OnResolvingAssembly(AssemblyLoadContext arg1, AssemblyName assemblyName)
        {
            // Log/handle failure
            return null;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            if (_runtimeAssemblies.Value.Contains(assemblyName.Name))
            {
                return null;
            }

            string path = Path.Combine(_baseProbingPath, assemblyName.Name + ".dll");

            if (File.Exists(path))
            {
                return LoadFromAssemblyPath(path);
            }

            return null;
        }

        protected static string ResolveFunctionAppRoot()
        {
            var settingsManager = ScriptSettingsManager.Instance;
            if (settingsManager.IsAzureEnvironment)
            {
                string home = settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteHomePath);
                return Path.Combine(home, "site", "wwwroot");
            }

            return settingsManager.GetSetting(EnvironmentSettingNames.AzureWebJobsScriptRoot);
        }

        private static string[] GetRuntimeAssemblies()
        {
            string assembliesJson = GetRuntimeAssembliesJson();
            JObject assemblies = JObject.Parse(assembliesJson);

            return assemblies["runtimeAssemblies"].ToObject<string[]>();
        }

        private static string GetRuntimeAssembliesJson()
        {
            var assembly = typeof(FunctionAssemblyLoadContext).Assembly;
            using (Stream resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".runtimeassemblies.json"))
            using (var reader = new StreamReader(resource))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
