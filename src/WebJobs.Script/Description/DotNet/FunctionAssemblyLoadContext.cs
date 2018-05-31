// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyModel;
using Newtonsoft.Json.Linq;
using static Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment;
using ResolutionPolicyEvaluator = System.Func<System.Reflection.AssemblyName, System.Reflection.Assembly, bool>;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Establishes an assembly load context for a extensions, functions and their dependencies.
    /// </summary>
    public partial class FunctionAssemblyLoadContext : AssemblyLoadContext
    {
        private static readonly Lazy<Dictionary<string, ScriptRuntimeAssembly>> _runtimeAssemblies = new Lazy<Dictionary<string, ScriptRuntimeAssembly>>(GetRuntimeAssemblies);
        private static readonly Lazy<Dictionary<string, ResolutionPolicyEvaluator>> _resolutionPolicyEvaluators = new Lazy<Dictionary<string, ResolutionPolicyEvaluator>>(InitializeLoadPolicyEvaluators);
        private static Lazy<FunctionAssemblyLoadContext> _defaultContext = new Lazy<FunctionAssemblyLoadContext>(() => new FunctionAssemblyLoadContext(ResolveFunctionBaseProbingPath()), true);

        private readonly List<string> _probingPaths = new List<string>();

        public FunctionAssemblyLoadContext(string basePath)
        {
            if (basePath == null)
            {
                throw new ArgumentNullException(nameof(basePath));
            }

            _probingPaths.Add(basePath);
        }

        public static FunctionAssemblyLoadContext Shared => _defaultContext.Value;

        private static Dictionary<string, ResolutionPolicyEvaluator> InitializeLoadPolicyEvaluators()
        {
            return new Dictionary<string, ResolutionPolicyEvaluator>
            {
                { "minorMatchOrLower", IsMinorMatchOrLowerPolicyEvaluator },
                { "runtimeVersion", RuntimeVersionPolicyEvaluator }
            };
        }

        /// <summary>
        /// A load policy evaluator that accepts the runtime assembly, regardless of version.
        /// </summary>
        /// <param name="requestedAssembly">The name of the requested assembly.</param>
        /// <param name="runtimeAssembly">The runtime assembly.</param>
        /// <returns>True if the evaluation succeeds.</returns>
        private static bool RuntimeVersionPolicyEvaluator(AssemblyName requestedAssembly, Assembly runtimeAssembly) => true;

        /// <summary>
        /// A load policy evaluator that verifies if the runtime package is the same major and
        /// newer minor version.
        /// </summary>
        /// <param name="requestedAssembly">The name of the requested assembly.</param>
        /// <param name="runtimeAssembly">The runtime assembly.</param>
        /// <returns>True if the evaluation succeeds.</returns>
        private static bool IsMinorMatchOrLowerPolicyEvaluator(AssemblyName requestedAssembly, Assembly runtimeAssembly)
        {
            AssemblyName runtimeAssemblyName = AssemblyNameCache.GetName(runtimeAssembly);

            return requestedAssembly.Version.Major == runtimeAssemblyName.Version.Major &&
                requestedAssembly.Version.Minor <= runtimeAssemblyName.Version.Minor;
        }

        private bool IsRuntimeAssembly(AssemblyName assemblyName)
            => _runtimeAssemblies.Value.ContainsKey(assemblyName.Name);

        private bool TryGetRuntimeAssembly(AssemblyName assemblyName, out ScriptRuntimeAssembly assembly)
            => _runtimeAssemblies.Value.TryGetValue(assemblyName.Name, out assembly);

        private ResolutionPolicyEvaluator GetResolutionPolicyEvaluator(string policyName)
        {
            if (_resolutionPolicyEvaluators.Value.TryGetValue(policyName, out ResolutionPolicyEvaluator policy))
            {
                return policy;
            }

            throw new InvalidOperationException($"'{policyName}' is not a valid assembly resolution policy");
        }

        private Assembly LoadCore(AssemblyName assemblyName)
        {
            foreach (var probingPath in _probingPaths)
            {
                string path = Path.Combine(probingPath, assemblyName.Name + ".dll");

                if (File.Exists(path))
                {
                    return LoadFromAssemblyPath(path);
                }
            }

            return null;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // If the assembly being requested matches a runtime assembly,
            // we'll use the runtime assembly instead of loading it in this context:
            if (TryLoadRuntimeAssembly(assemblyName, out Assembly assembly))
            {
                return assembly;
            }

            if (TryGetRuntimeAssembly(assemblyName, out ScriptRuntimeAssembly scriptRuntimeAssembly))
            {
                // If there was a failure loading a runtime assembly, ensure we gracefuly unify to
                // the runtime version of the assembly if the version falls within a safe range.
                if (TryLoadRuntimeAssembly(new AssemblyName(assemblyName.Name), out assembly))
                {
                    var policyEvaluator = GetResolutionPolicyEvaluator(scriptRuntimeAssembly.ResolutionPolicy);

                    if (policyEvaluator.Invoke(assemblyName, assembly))
                    {
                        return assembly;
                    }
                }

                return null;
            }

            return LoadCore(assemblyName);
        }

        private bool TryLoadRuntimeAssembly(AssemblyName assemblyName, out Assembly assembly)
        {
            assembly = null;
            try
            {
                assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
            }

            return assembly != null;
        }

        public Assembly LoadFromAssemblyPath(string assemblyPath, bool addProbingPath)
        {
            if (addProbingPath)
            {
                string directory = Path.GetDirectoryName(assemblyPath);

                if (Directory.Exists(directory))
                {
                    _probingPaths.Add(directory);
                }
            }

            return LoadFromAssemblyPath(assemblyPath);
        }

        internal (bool succeeded, Assembly assembly, bool isRuntimeAssembly) TryLoadAssembly(AssemblyName assemblyName)
        {
            bool isRuntimeAssembly = IsRuntimeAssembly(assemblyName);

            Assembly assembly = null;
            if (!isRuntimeAssembly)
            {
                assembly = Load(assemblyName);
            }

            return (assembly != null, assembly, isRuntimeAssembly);
        }

        protected virtual Assembly OnResolvingAssembly(AssemblyLoadContext arg1, AssemblyName assemblyName)
        {
            // Log/handle failure
            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string fileName = GetUnmanagedLibraryFileName(unmanagedDllName);
            string filePath = GetRuntimeAssetPath(fileName, true);

            if (filePath != null)
            {
                return LoadUnmanagedDllFromPath(filePath);
            }

            return base.LoadUnmanagedDll(unmanagedDllName);
        }

        private string GetRuntimeAssetPath(string assetFileName, bool isNativeAsset)
        {
            string basePath = _probingPaths[0];
            string ridSubFolder = isNativeAsset ? "native" : string.Empty;
            string runtimesPath = Path.Combine(basePath, "runtimes");

            string currentRuntimeIdentifier = GetRuntimeIdentifier();
            RuntimeFallbacks fallbacks = DependencyContext.Default
                .RuntimeGraph
                .FirstOrDefault(f => string.Equals(f.Runtime, currentRuntimeIdentifier, StringComparison.OrdinalIgnoreCase));

            var rids = new List<string> { fallbacks.Runtime };
            rids.AddRange(fallbacks.Fallbacks);

            return rids.Select(r => Path.Combine(runtimesPath, r, ridSubFolder, assetFileName))
                .FirstOrDefault(p => File.Exists(p));
        }

        internal string GetUnmanagedLibraryFileName(string unmanagedLibraryName)
        {
            // We need to properly resolve the native library in different platforms:
            // - Windows will append the '.DLL' extension to the name
            // - Linux uses the 'lib' prefix and '.so' suffix. The version may also be appended to the suffix
            // - macOS uses the 'lib' prefix '.dylib'
            // To handle the different scenarios described above, we'll just have a pattern that gives us the ability
            // to match the variations across different platforms. If needed, we can expand this in the future to have
            // logic specific to the platform we're running under.

            string fileName = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (unmanagedLibraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = unmanagedLibraryName;
                }
                else
                {
                    fileName = unmanagedLibraryName + ".dll";
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                fileName = "lib" + unmanagedLibraryName + ".dylib";
            }
            else
            {
                fileName = "lib" + unmanagedLibraryName + ".so";
            }

            return fileName;
        }

        protected static string ResolveFunctionBaseProbingPath()
        {
            string basePath = null;

            if (ScriptSettingsManager.Instance.IsAppServiceEnvironment)
            {
                string home = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHomePath);
                basePath = Path.Combine(home, "site", "wwwroot");
            }
            else
            {
                basePath = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsScriptRoot) ?? AppContext.BaseDirectory;
            }

            return Path.Combine(basePath, "bin");
        }

        private static Dictionary<string, ScriptRuntimeAssembly> GetRuntimeAssemblies()
        {
            string assembliesJson = GetRuntimeAssembliesJson();
            JObject assemblies = JObject.Parse(assembliesJson);

            return assemblies["runtimeAssemblies"]
                .ToObject<ScriptRuntimeAssembly[]>()
                .ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
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
