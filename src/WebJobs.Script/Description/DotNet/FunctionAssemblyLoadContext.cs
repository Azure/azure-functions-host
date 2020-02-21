// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyModel;
using ResolutionPolicyEvaluator = System.Func<System.Reflection.AssemblyName, System.Reflection.Assembly, bool>;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Establishes an assembly load context for a extensions, functions and their dependencies.
    /// </summary>
    public partial class FunctionAssemblyLoadContext : AssemblyLoadContext
    {
        private const string PrivateDependencyResolutionPolicy = "private";

        private static readonly Lazy<Dictionary<string, ScriptRuntimeAssembly>> _runtimeAssemblies = new Lazy<Dictionary<string, ScriptRuntimeAssembly>>(DependencyHelper.GetRuntimeAssemblies);
        private static readonly Lazy<Dictionary<string, ResolutionPolicyEvaluator>> _resolutionPolicyEvaluators = new Lazy<Dictionary<string, ResolutionPolicyEvaluator>>(InitializeLoadPolicyEvaluators);
        private static readonly ConcurrentDictionary<string, object> _sharedContextAssembliesInFallbackLoad = new ConcurrentDictionary<string, object>();

        private static Lazy<FunctionAssemblyLoadContext> _defaultContext = new Lazy<FunctionAssemblyLoadContext>(CreateSharedContext, true);

        private readonly List<string> _probingPaths = new List<string>();
        private readonly IDictionary<string, RuntimeAsset[]> _depsAssemblies;
        private readonly IDictionary<string, RuntimeAsset[]> _nativeLibraries;
        private readonly List<string> _currentRidFallback;

        public FunctionAssemblyLoadContext(string basePath)
        {
            if (basePath == null)
            {
                throw new ArgumentNullException(nameof(basePath));
            }

            _currentRidFallback = DependencyHelper.GetRuntimeFallbacks();

            (_depsAssemblies, _nativeLibraries) = InitializeDeps(basePath, _currentRidFallback);

            _probingPaths.Add(basePath);
        }

        public static FunctionAssemblyLoadContext Shared => _defaultContext.Value;

        internal static void ResetSharedContext()
        {
            _defaultContext = new Lazy<FunctionAssemblyLoadContext>(CreateSharedContext, true);
        }

        internal static (IDictionary<string, RuntimeAsset[]> depsAssemblies, IDictionary<string, RuntimeAsset[]> nativeLibraries) InitializeDeps(string basePath, List<string> ridFallbacks)
        {
            string depsFilePath = Path.Combine(basePath, DotNetConstants.FunctionsDepsFileName);

            if (File.Exists(depsFilePath))
            {
                try
                {
                    var reader = new DependencyContextJsonReader();
                    using (Stream file = File.OpenRead(depsFilePath))
                    {
                        var depsContext = reader.Read(file);
                        var depsAssemblies = depsContext.RuntimeLibraries.SelectMany(l => SelectRuntimeAssemblyGroup(ridFallbacks, l.RuntimeAssemblyGroups))
                            .GroupBy(a => Path.GetFileNameWithoutExtension(a.Path))
                            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

                        // Note the difference here that nativeLibraries has the whole file name, including extension.
                        var nativeLibraries = depsContext.RuntimeLibraries.SelectMany(l => SelectRuntimeAssemblyGroup(ridFallbacks, l.NativeLibraryGroups))
                            .GroupBy(path => Path.GetFileName(path.Path))
                            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

                        return (depsAssemblies, nativeLibraries);
                    }
                }
                catch
                {
                }
            }

            return (null, null);
        }

        private static IEnumerable<RuntimeAsset> SelectRuntimeAssemblyGroup(List<string> rids, IReadOnlyList<RuntimeAssetGroup> runtimeAssemblyGroups)
        {
            // Attempt to load group for the current RID graph
            foreach (var rid in rids)
            {
                var assemblyGroup = runtimeAssemblyGroups.FirstOrDefault(g => string.Equals(g.Runtime, rid, StringComparison.OrdinalIgnoreCase));
                if (assemblyGroup != null)
                {
                    return assemblyGroup.AssetPaths.Select(path => new RuntimeAsset(rid, path));
                }
            }

            // If unsuccessful, load default assets, making sure the path is flattened to reflect deployed files
            return runtimeAssemblyGroups.GetDefaultAssets().Select(a => new RuntimeAsset(null, Path.GetFileName(a)));
        }

        private static FunctionAssemblyLoadContext CreateSharedContext()
        {
            var sharedContext = new FunctionAssemblyLoadContext(ResolveFunctionBaseProbingPath());
            Default.Resolving += HandleDefaultContextFallback;

            return sharedContext;
        }

        private static Assembly HandleDefaultContextFallback(AssemblyLoadContext loadContext, AssemblyName assemblyName)
        {
            // If we're not currently loading this from the shared context, an attempt to load
            // a user assembly from the default context might have been made (e.g. Assembly.Load or AppDomain.Load called in a
            // runtime/default context loaded assembly against a function assembly)
            if (!_sharedContextAssembliesInFallbackLoad.ContainsKey(assemblyName.Name))
            {
                return Shared.LoadCore(assemblyName);
            }

            return null;
        }

        private static Dictionary<string, ResolutionPolicyEvaluator> InitializeLoadPolicyEvaluators()
        {
            return new Dictionary<string, ResolutionPolicyEvaluator>
            {
                { "minorMatchOrLower", IsMinorMatchOrLowerPolicyEvaluator },
                { "runtimeVersion", RuntimeVersionPolicyEvaluator },
                { PrivateDependencyResolutionPolicy, (a, b) => false }
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

            return requestedAssembly.Version == null || (requestedAssembly.Version.Major == runtimeAssemblyName.Version.Major &&
                requestedAssembly.Version.Minor <= runtimeAssemblyName.Version.Minor);
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
            // Try to load from deps references, if available
            if (TryLoadDepsDependency(assemblyName, out Assembly assembly))
            {
                return assembly;
            }

            // If this is a runtime restricted assembly, load it based on unification rules
            if (TryGetRuntimeAssembly(assemblyName, out ScriptRuntimeAssembly scriptRuntimeAssembly))
            {
                return LoadRuntimeAssembly(assemblyName, scriptRuntimeAssembly);
            }

            // If the assembly being requested matches a host assembly, we'll use
            // the host assembly instead of loading it in this context:
            if (TryLoadHostEnvironmentAssembly(assemblyName, out assembly))
            {
                return assembly;
            }

            return LoadCore(assemblyName);
        }

        private Assembly LoadRuntimeAssembly(AssemblyName assemblyName, ScriptRuntimeAssembly scriptRuntimeAssembly)
        {
            // If this is a private runtime assembly, return function dependency
            if (string.Equals(scriptRuntimeAssembly.ResolutionPolicy, PrivateDependencyResolutionPolicy))
            {
                return LoadCore(assemblyName);
            }

            // Attempt to load the runtime version of the assembly based on the unification policy evaluation result.
            if (TryLoadHostEnvironmentAssembly(assemblyName, allowPartialNameMatch: true, out Assembly assembly))
            {
                var policyEvaluator = GetResolutionPolicyEvaluator(scriptRuntimeAssembly.ResolutionPolicy);

                if (policyEvaluator.Invoke(assemblyName, assembly))
                {
                    return assembly;
                }
            }

            return null;
        }

        private bool TryLoadDepsDependency(AssemblyName assemblyName, out Assembly assembly)
        {
            assembly = null;

            if (_depsAssemblies != null &&
                !IsRuntimeAssembly(assemblyName) &&
                TryGetDepsAsset(_depsAssemblies, assemblyName.Name, _currentRidFallback, out string assemblyPath))
            {
                foreach (var probingPath in _probingPaths)
                {
                    string filePath = Path.Combine(probingPath, assemblyPath);
                    if (File.Exists(filePath))
                    {
                        assembly = LoadFromAssemblyPath(filePath);
                        break;
                    }
                }
            }

            return assembly != null;
        }

        internal static bool TryGetDepsAsset(IDictionary<string, RuntimeAsset[]> depsAssets, string assetName, List<string> ridFallbacks, out string assemblyPath)
        {
            assemblyPath = null;

            if (depsAssets.TryGetValue(assetName, out RuntimeAsset[] assets))
            {
                // If we have a single asset match, return it:
                if (assets.Length == 1)
                {
                    assemblyPath = assets[0].Path;
                }
                else
                {
                    foreach (var rid in ridFallbacks)
                    {
                        RuntimeAsset match = assets.FirstOrDefault(a => string.Equals(rid, a.Rid, StringComparison.OrdinalIgnoreCase));

                        if (match != null)
                        {
                            assemblyPath = match.Path;
                            break;
                        }
                    }

                    // If we're unable to locate a matching asset based on the RID fallback probing,
                    // attempt to use a default/RID-agnostic asset instead
                    if (assemblyPath == null)
                    {
                        assemblyPath = assets.FirstOrDefault(a => a.Rid == null)?.Path;
                    }
                }
            }

            return assemblyPath != null;
        }

        private bool TryLoadHostEnvironmentAssembly(AssemblyName assemblyName, bool allowPartialNameMatch, out Assembly assembly)
        {
            return TryLoadHostEnvironmentAssembly(assemblyName, out assembly)
                || (allowPartialNameMatch && TryLoadHostEnvironmentAssembly(new AssemblyName(assemblyName.Name), out assembly));
        }

        private bool TryLoadHostEnvironmentAssembly(AssemblyName assemblyName, out Assembly assembly)
        {
            assembly = null;
            try
            {
                _sharedContextAssembliesInFallbackLoad.TryAdd(assemblyName.Name, null);
                assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
            }
            finally
            {
                _sharedContextAssembliesInFallbackLoad.TryRemove(assemblyName.Name, out object _);
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
            string filePath = GetRuntimeNativeAssetPath(fileName);

            if (filePath != null)
            {
                return LoadUnmanagedDllFromPath(filePath);
            }

            return base.LoadUnmanagedDll(unmanagedDllName);
        }

        private string GetRuntimeNativeAssetPath(string assetFileName)
        {
            string basePath = _probingPaths[0];
            const string ridSubFolder = "native";
            string runtimesPath = Path.Combine(basePath, "runtimes");

            List<string> rids = DependencyHelper.GetRuntimeFallbacks();

            string result = rids.Select(r => Path.Combine(runtimesPath, r, ridSubFolder, assetFileName))
                .Union(_probingPaths)
                .FirstOrDefault(p => File.Exists(p));

            if (result == null && _nativeLibraries != null)
            {
                if (TryGetDepsAsset(_nativeLibraries, assetFileName, _currentRidFallback, out string relativePath))
                {
                    string nativeLibraryFullPath = Path.Combine(basePath, relativePath);
                    if (File.Exists(nativeLibraryFullPath))
                    {
                        result = nativeLibraryFullPath;
                    }
                }
            }

            return result;
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
    }
}
