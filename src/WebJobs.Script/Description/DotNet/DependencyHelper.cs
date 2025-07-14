// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Description.DotNet;
using Microsoft.Azure.WebJobs.Script.ExtensionRequirements;
using Microsoft.Extensions.DependencyModel;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public static class DependencyHelper
    {
        private const string AssemblyNamePrefix = "assembly:";
        private static readonly Assembly ThisAssembly = typeof(DependencyHelper).Assembly;
        private static readonly string ThisAssemblyName = ThisAssembly.GetName().Name;
        private static readonly Lazy<Dictionary<string, string[]>> RidGraph = new Lazy<Dictionary<string, string[]>>(BuildRuntimesGraph);

        private static string _runtimeIdentifier;

        private static Dictionary<string, string[]> BuildRuntimesGraph()
        {
            using var stream = GetEmbeddedResourceStream("runtimes.json");

            var runtimeGraph = JsonSerializer.Deserialize(stream, RuntimeGraphJsonContext.Default.RuntimeGraph);

            if (runtimeGraph is not { Runtimes.Count: > 0 })
            {
                throw new InvalidOperationException("Failed to deserialize runtimes graph JSON or runtimes section is empty.");
            }

            var ridGraph = new Dictionary<string, string[]>(runtimeGraph.Runtimes.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var (rid, info) in runtimeGraph.Runtimes)
            {
                ridGraph[rid] = info.Imports ?? [];
            }

            return ridGraph;
        }

        private static string GetDefaultPlatformRid()
        {
            // This logic follows what the .NET Core host does in: https://github.com/dotnet/core-setup/blob/master/src/corehost/common/pal.h

            // When running on a platform that is not supported in RID fallback graph (because it was unknown
            // at the time the SharedFX in question was built), we need to use a reasonable fallback RID to allow
            // consuming the native assets.
            //
            // For Windows and OSX, we will maintain the last highest RID-Platform we are known to support for them as the
            // degree of compat across their respective releases is usually high.
            //
            // We cannot maintain the same (compat) invariant for linux and thus, we will fallback to using lowest RID-Plaform.

            string rid = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                rid = DotNetConstants.DefaultWindowsRID;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                rid = DotNetConstants.DefaultOSXRID;
            }
            else
            {
                rid = DotNetConstants.DefaultLinuxRID;
            }

            return rid;
        }

        private static Stream GetEmbeddedResourceStream(string fileName)
        {
            var stream = ThisAssembly.GetManifestResourceStream($"{ThisAssemblyName}.{fileName}");

            return stream ?? throw new InvalidOperationException($"The embedded resource '{ThisAssemblyName}.{fileName}' could not be found.");
        }

        internal static Dictionary<string, ScriptRuntimeAssembly> GetRuntimeAssemblies(string assemblyManifestName)
        {
            using var stream = GetEmbeddedResourceStream(assemblyManifestName);
            var runtimeAssemblies = JsonSerializer.Deserialize(stream, RuntimeAssembliesJsonContext.Default.RuntimeAssembliesConfig);

            var assemblies = runtimeAssemblies?.RuntimeAssemblies ?? throw new InvalidOperationException($"Failed to retrieve runtime assemblies from the embedded resource '{assemblyManifestName}'.");

            var dictionary = new Dictionary<string, ScriptRuntimeAssembly>(assemblies.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in assemblies)
            {
                dictionary[assembly.Name] = assembly;
            }

            return dictionary;
        }

        internal static ExtensionRequirementsInfo GetExtensionRequirements()
        {
            const string fileName = "extensionrequirements.json";

            using var stream = GetEmbeddedResourceStream(fileName);
            var extensionRequirementsInfo = JsonSerializer.Deserialize(stream, ExtensionRequirementsJsonContext.Default.ExtensionRequirementsInfo);

            if (extensionRequirementsInfo is null)
            {
                throw new InvalidOperationException($"Failed to deserialize extension requirements from embedded resource '{fileName}'.");
            }

            return extensionRequirementsInfo;
        }

        /// <summary>
        /// Gets the default runtime fallback RIDs for a given RID.
        /// The graph used to build the fallback list is static and
        /// useful in self-contained scenarios, where this information
        /// is not available at runtime
        /// </summary>
        /// <param name="rid">The runtime identifier to lookup.</param>
        /// <returns>The runtime fallbacks for the provided identifier.</returns>
        public static RuntimeFallbacks GetDefaultRuntimeFallbacks(string rid)
        {
            var ridGraph = RidGraph.Value;

            var runtimeFallbacks = new RuntimeFallbacks(rid);
            var fallbacks = new List<string>();

            if (!ridGraph.ContainsKey(rid))
            {
                rid = GetDefaultPlatformRid();
                fallbacks.Add(rid);
            }

            var queue = new Queue<string>(ridGraph[rid]);

            while (queue.Count > 0)
            {
                var currentRid = queue.Dequeue();

                if (fallbacks.Contains(currentRid))
                {
                    continue;
                }

                fallbacks.Add(currentRid);

                foreach (var fallbackRid in ridGraph[currentRid])
                {
                    if (!fallbacks.Contains(fallbackRid, StringComparer.OrdinalIgnoreCase))
                    {
                        queue.Enqueue(fallbackRid);
                    }
                }
            }

            runtimeFallbacks.Fallbacks = fallbacks.AsReadOnly();
            return runtimeFallbacks;
        }

        public static List<string> GetRuntimeFallbacks()
        {
            string currentRuntimeIdentifier = GetRuntimeIdentifier();

            return GetRuntimeFallbacks(currentRuntimeIdentifier);
        }

        public static List<string> GetRuntimeFallbacks(string rid)
        {
            if (rid == null)
            {
                throw new ArgumentNullException(nameof(rid));
            }

            RuntimeFallbacks fallbacks = DependencyContext.Default
                .RuntimeGraph
                .FirstOrDefault(f => string.Equals(f.Runtime, rid, StringComparison.OrdinalIgnoreCase))
                ?? GetDefaultRuntimeFallbacks(rid)
                ?? new RuntimeFallbacks("any");

            var rids = new List<string> { fallbacks.Runtime };
            rids.AddRange(fallbacks.Fallbacks);
            return rids;
        }

        /// <summary>
        /// Checks if the string is in assembly representation format.
        /// </summary>
        /// <param name="assemblyFormatString"> string representing assembly information</param>
        /// <returns> bool if string in was in proper assembly representation format. </returns>
        public static bool IsAssemblyReferenceFormat(string assemblyFormatString)
        {
            return assemblyFormatString != null && assemblyFormatString.StartsWith(AssemblyNamePrefix);
        }

        /// <summary>
        /// Gets the Assembly name from the assembly path string, if in the expected format for an assembly reference.
        /// </summary>
        /// <param name="assemblyFormatString"> The assembly name string in the expected format. </param>
        /// <returns> bool if the string was in the proper assembly format. </returns>
        public static bool TryGetAssemblyReference(string assemblyFormatString, out string assemblyName)
        {
            assemblyName = null;

            var isSharedAssembly = IsAssemblyReferenceFormat(assemblyFormatString);
            if (isSharedAssembly)
            {
                assemblyName = assemblyFormatString.Substring(AssemblyNamePrefix.Length);
            }

            return isSharedAssembly;
        }

        private static string GetRuntimeIdentifier() => _runtimeIdentifier ??= AppContext.GetData("RUNTIME_IDENTIFIER") as string ?? "unknown";
    }
}
