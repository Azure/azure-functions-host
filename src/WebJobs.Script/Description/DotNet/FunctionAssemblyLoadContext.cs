// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
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

        private static readonly Lazy<Dictionary<string, ResolutionPolicyEvaluator>> _resolutionPolicyEvaluators = new Lazy<Dictionary<string, ResolutionPolicyEvaluator>>(InitializeLoadPolicyEvaluators);
        private static readonly ConcurrentDictionary<string, int> _sharedContextAssembliesInFallbackLoad = new ConcurrentDictionary<string, int>();
        private static readonly RuntimeAssembliesInfo _runtimeAssembliesInfo = new RuntimeAssembliesInfo();

        private static Lazy<FunctionAssemblyLoadContext> _defaultContext = new(() => CreateSharedContext(ResolveFunctionBaseProbingPath()), true);

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

        internal RuntimeAssembliesInfo RuntimeAssemblies => _runtimeAssembliesInfo;

        internal static void ResetSharedContext(string baseProbingPath = null)
        {
            baseProbingPath ??= ResolveFunctionBaseProbingPath();

            _defaultContext = new Lazy<FunctionAssemblyLoadContext>(() => CreateSharedContext(baseProbingPath), true);
            _runtimeAssembliesInfo.ResetIfStale();
        }

        internal static (IDictionary<string, RuntimeAsset[]> DepsAssemblies, IDictionary<string, RuntimeAsset[]> NativeLibraries) InitializeDeps(string basePath, List<string> ridFallbacks)
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
                    return assemblyGroup.RuntimeFiles.Select(file => new RuntimeAsset(rid, file.Path, file.AssemblyVersion));
                }
            }

            // If unsuccessful, load default assets, making sure the path is flattened to reflect deployed files
            return runtimeAssemblyGroups.GetDefaultRuntimeFileAssets().Select(a => new RuntimeAsset(null, Path.GetFileName(a.Path), a.AssemblyVersion));
        }

        private static FunctionAssemblyLoadContext CreateSharedContext(string baseProbingPath)
        {
            var sharedContext = new FunctionAssemblyLoadContext(baseProbingPath);
            Default.Resolving += HandleDefaultContextFallback;

            return sharedContext;
        }

        private static Assembly HandleDefaultContextFallback(AssemblyLoadContext loadContext, AssemblyName assemblyName)
        {
            // If we're not currently loading this from the shared context, an attempt to load
            // a user assembly from the default context might have been made (e.g. Assembly.Load or AppDomain.Load called in a
            // runtime/default context loaded assembly against a function assembly)
            if (!_sharedContextAssembliesInFallbackLoad.TryGetValue(assemblyName.Name, out int count)
                || count == 0)
            {
                if (Shared.TryLoadDepsDependency(assemblyName, out Assembly assembly))
                {
                    return assembly;
                }

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
        {
            return _runtimeAssembliesInfo.Assemblies.TryGetValue(assemblyName.Name, out ScriptRuntimeAssembly assembly)
                && !string.Equals(assembly.ResolutionPolicy, PrivateDependencyResolutionPolicy, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetRuntimeAssembly(AssemblyName assemblyName, out ScriptRuntimeAssembly assembly)
            => _runtimeAssembliesInfo.Assemblies.TryGetValue(assemblyName.Name, out assembly);

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
            bool isNameAdjusted = TryAdjustRuntimeAssemblyFromDepsFile(assemblyName, out assemblyName);

            // Try to load from deps references, if available
            if (TryLoadDepsDependency(assemblyName, out Assembly assembly))
            {
                return assembly;
            }

            // If this is a runtime restricted assembly, load it based on unification rules
            if (TryGetRuntimeAssembly(assemblyName, out ScriptRuntimeAssembly scriptRuntimeAssembly))
            {
                // There are several possible scenarios:
                //  1. The assembly was found and the policy evaluator succeeded.
                //     - Return the assembly.
                //
                //  2. The assembly was not found (meaning the policy evaluator wasn't able to run).
                //     - Return null as there's not much else we can do. This will fail and come back to this context
                //       through the fallback logic for another attempt. This is likely an error case so let it fail.
                //
                //  3. The assembly was found but the policy evaluator failed.
                //     a. We did not adjust the requested assembly name via the deps file.
                //        - This means that the DefaultLoadContext is looking for the correct version. Return null and let
                //          it handle the load attempt.
                //     b. We adjusted the requested assembly name via the deps file. If we return null the DefaultLoadContext would attempt to
                //        load the original assembly version, which may be incorrect if we had to adjust it forward past the runtime's version.
                //        i.  The adjusted assembly name is higher than the runtime's version.
                //            - Do not trust the DefaultLoadContext to handle this as it may resolve to the runtime's assembly. Instead,
                //              call LoadCore() to ensure the adjusted assembly is loaded.
                //        ii. The adjusted assembly name is still lower than or equal to the runtime's version (this likely means
                //            a lower major version).
                //            - Return null and let the DefaultLoadContext handle it. It may come back here due to fallback logic, but
                //              ultimately it will unify on the runtime version.

                if (TryLoadRuntimeAssembly(assemblyName, scriptRuntimeAssembly, out assembly))
                {
                    // Scenarios 1 and 2(a).
                    return assembly;
                }
                else
                {
                    if (assembly == null || !isNameAdjusted)
                    {
                        // Scenario 2 and 3(a).
                        return null;
                    }

                    AssemblyName runtimeAssemblyName = AssemblyNameCache.GetName(assembly);
                    if (assemblyName.Version > runtimeAssemblyName.Version)
                    {
                        // Scenario 3(b)(i).
                        return LoadCore(assemblyName);
                    }

                    // Scenario 3(b)(ii).
                    return null;
                }
            }

            // If the assembly being requested matches a host assembly, we'll use
            // the host assembly instead of loading it in this context:
            if (TryLoadHostEnvironmentAssembly(assemblyName, out assembly))
            {
                return assembly;
            }

            return LoadCore(assemblyName);
        }

        private bool TryAdjustRuntimeAssemblyFromDepsFile(AssemblyName assemblyName, out AssemblyName newAssemblyName)
        {
            newAssemblyName = assemblyName;

            // It's possible for references to depend on different versions, and deps.json is the
            // source of truth for which version should be resolved.
            if (IsRuntimeAssembly(assemblyName) &&
                TryGetDepsAsset(_depsAssemblies, assemblyName.Name, _currentRidFallback, out RuntimeAsset asset)
                && asset.AssemblyVersion != assemblyName.Version)
            {
                // We'll be using the deps.json version
                newAssemblyName = new AssemblyName
                {
                    Name = assemblyName.Name,
                    Version = asset.AssemblyVersion
                };

                return true;
            }

            return false;
        }

        /// <summary>
        /// Loads the runtime's version of this assembly and runs it through the appropriate policy evaluator.
        /// </summary>
        /// <param name="assemblyName">The assembly name to load.</param>
        /// <param name="scriptRuntimeAssembly">The policy evaluator details.</param>
        /// <param name="runtimeAssembly">The runtime's assembly.</param>
        /// <returns>true if the policy evaluation succeeded, otherwise, false.</returns>
        private bool TryLoadRuntimeAssembly(AssemblyName assemblyName, ScriptRuntimeAssembly scriptRuntimeAssembly, out Assembly runtimeAssembly)
        {
            // If this is a private runtime assembly, return function dependency
            if (string.Equals(scriptRuntimeAssembly.ResolutionPolicy, PrivateDependencyResolutionPolicy))
            {
                runtimeAssembly = LoadCore(assemblyName);
                return true;
            }

            // Attempt to load the runtime version of the assembly based on the unification policy evaluation result.
            if (TryLoadHostEnvironmentAssembly(assemblyName, allowPartialNameMatch: true, out runtimeAssembly))
            {
                var policyEvaluator = GetResolutionPolicyEvaluator(scriptRuntimeAssembly.ResolutionPolicy);
                return policyEvaluator.Invoke(assemblyName, runtimeAssembly);
            }

            runtimeAssembly = null;
            return false;
        }

        private bool TryLoadDepsDependency(AssemblyName assemblyName, out Assembly assembly)
        {
            assembly = null;

            if (_depsAssemblies != null &&
                !IsRuntimeAssembly(assemblyName) &&
                TryGetDepsAsset(_depsAssemblies, assemblyName.Name, _currentRidFallback, out RuntimeAsset asset))
            {
                foreach (var probingPath in _probingPaths)
                {
                    string filePath = Path.Combine(probingPath, asset.Path);
                    if (File.Exists(filePath))
                    {
                        assembly = LoadFromAssemblyPath(filePath);
                        break;
                    }
                }
            }

            return assembly != null;
        }

        internal static bool TryGetDepsAsset(IDictionary<string, RuntimeAsset[]> depsAssets, string assetName, List<string> ridFallbacks, out RuntimeAsset asset)
        {
            asset = null;

            if (depsAssets != null && depsAssets.TryGetValue(assetName, out RuntimeAsset[] assets))
            {
                // If we have a single asset match, return it:
                if (assets.Length == 1)
                {
                    asset = assets[0];
                }
                else
                {
                    foreach (var rid in ridFallbacks)
                    {
                        RuntimeAsset match = assets.FirstOrDefault(a => string.Equals(rid, a.Rid, StringComparison.OrdinalIgnoreCase));

                        if (match != null)
                        {
                            asset = match;
                            break;
                        }
                    }

                    // If we're unable to locate a matching asset based on the RID fallback probing,
                    // attempt to use a default/RID-agnostic asset instead
                    if (asset == null)
                    {
                        asset = assets.FirstOrDefault(a => a.Rid == null);
                    }
                }
            }

            return asset != null;
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
                _sharedContextAssembliesInFallbackLoad.AddOrUpdate(assemblyName.Name, 1, (s, i) => i + 1);
                assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
            }
            finally
            {
                _sharedContextAssembliesInFallbackLoad.AddOrUpdate(assemblyName.Name, 0, (s, i) => i - 1);
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

        internal (bool Succeeded, Assembly Assembly, bool IsRuntimeAssembly) TryLoadAssembly(AssemblyName assemblyName)
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
            foreach (var fileName in GetUnmanagedLibraryFileNames(unmanagedDllName, SystemRuntimeInformation.Instance.GetOSPlatform()))
            {
                string filePath = GetRuntimeNativeAssetPath(fileName);

                if (filePath != null)
                {
                    return LoadUnmanagedDllFromPath(filePath);
                }
            }

            return base.LoadUnmanagedDll(unmanagedDllName);
        }

        internal string GetRuntimeNativeAssetPath(string assetFileName)
        {
            string result = ProbeForNativeAsset(_probingPaths, assetFileName, FileUtility.Instance.File);

            if (result == null && _nativeLibraries != null)
            {
                if (TryGetDepsAsset(_nativeLibraries, assetFileName, _currentRidFallback, out RuntimeAsset asset))
                {
                    string basePath = _probingPaths[0];

                    string nativeLibraryFullPath = Path.Combine(basePath, asset.Path);
                    if (File.Exists(nativeLibraryFullPath))
                    {
                        result = nativeLibraryFullPath;
                    }
                }
            }

            return result;
        }

        internal static string ProbeForNativeAsset(IList<string> probingPaths, string assetFileName, FileBase fileBase)
        {
            string basePath = probingPaths[0];
            const string ridSubFolder = "native";
            const string runtimesSubFolder = "runtimes";
            string runtimesPath = Path.Combine(basePath, runtimesSubFolder);

            List<string> rids = DependencyHelper.GetRuntimeFallbacks();

            foreach (string rid in rids)
            {
                string path = Path.Combine(runtimesPath, rid, ridSubFolder, assetFileName);
                if (fileBase.Exists(path))
                {
                    return path;
                }
            }

            foreach (var probingPath in probingPaths)
            {
                string path = Path.Combine(probingPath, assetFileName);

                if (fileBase.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        internal static IEnumerable<string> GetUnmanagedLibraryFileNames(string unmanagedLibraryName, OSPlatform platform)
        {
            // We need to properly resolve the native library in different platforms:
            // Windows:
            //  - Will append the '.dll' extension to the name, if it does not already have the extension.
            // Linux and macOS:
            // - Returns the following, in order (where prefix is 'lib' and suffix is '.so' for Linux and '.dylib' for macOS):
            //    - libname + suffix
            //    - prefix + libname + suffix **
            //    - libname
            //    - prefix + libname *
            // On Linux only, if the library name ends with the suffix or contains the suffix + '.' (i.e. .so or .so.)
            // - Returns the following, in order (where prefix is 'lib' and suffix is '.so'):
            //    - libname
            //    - prefix + libname *
            //    - libname + suffix
            //    - prefix + libname + suffix **
            // * Will only be returned if the path delimiter is not present in the library name
            // ** Would typically have the same path delimiter check, but we maintain this here for backwards compatibility

            if (platform == OSPlatform.Windows)
            {
                if (unmanagedLibraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    yield return unmanagedLibraryName;
                }
                else
                {
                    yield return unmanagedLibraryName + ".dll";
                }
            }
            else
            {
                const string prefix = "lib";
                string suffix = platform == OSPlatform.Linux
                    ? ".so"
                    : ".dylib";

                foreach (var name in GetUnixUnmanagedLibraryFileNames(unmanagedLibraryName, prefix, suffix, platform))
                {
                    yield return name;
                }
            }
        }

        private static IEnumerable<string> GetUnixUnmanagedLibraryFileNames(string unmanagedLibraryName, string prefix, string suffix, OSPlatform platform)
        {
            bool containsPathDelimiter = unmanagedLibraryName.Contains(Path.DirectorySeparatorChar);
            bool containsExtension = platform == OSPlatform.Linux
                && (unmanagedLibraryName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                || unmanagedLibraryName.Contains(suffix + ".", StringComparison.OrdinalIgnoreCase));

            if (containsExtension)
            {
                yield return unmanagedLibraryName;

                if (!containsPathDelimiter)
                {
                    yield return prefix + unmanagedLibraryName;
                }
            }

            yield return unmanagedLibraryName + suffix;
            yield return prefix + unmanagedLibraryName + suffix;

            if (!containsExtension)
            {
                yield return unmanagedLibraryName;

                if (!containsPathDelimiter)
                {
                    yield return prefix + unmanagedLibraryName;
                }
            }
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
