// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyModel;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public static class DependencyHelper
    {
        private static readonly Lazy<Dictionary<string, string[]>> _ridGraph = new Lazy<Dictionary<string, string[]>>(BuildRuntimesGraph);

        private static Dictionary<string, string[]> BuildRuntimesGraph()
        {
            var ridGraph = new Dictionary<string, string[]>();
            string runtimesJson = GetRuntimesGraphJson();
            var runtimes = (JObject)JObject.Parse(runtimesJson)["runtimes"];

            foreach (var runtime in runtimes)
            {
                string[] imports = ((JObject)runtime.Value)["#import"]
                    ?.Values<string>()
                    .ToArray();

                ridGraph.Add(runtime.Key, imports);
            }

            return ridGraph;
        }

        public static IEnumerable<string> GetFallbacks(Dictionary<string, string[]> ridGraph, string rid)
        {
            var fallbacks = new HashSet<string>();
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
                    if (!fallbacks.Contains(fallbackRid))
                    {
                        queue.Enqueue(fallbackRid);
                    }
                }
            }

            return fallbacks;
        }

        private static string GetRuntimeAssembliesJson()
        {
            return GetResourceFileContents("runtimeassemblies.json");
        }

        private static string GetRuntimesGraphJson()
        {
            return GetResourceFileContents("runtimes.json");
        }

        private static string GetResourceFileContents(string fileName)
        {
            var assembly = typeof(DependencyHelper).Assembly;
            using (Stream resource = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.{fileName}"))
            using (var reader = new StreamReader(resource))
            {
                return reader.ReadToEnd();
            }
        }

        internal static Dictionary<string, ScriptRuntimeAssembly> GetRuntimeAssemblies()
        {
            string assembliesJson = GetRuntimeAssembliesJson();
            JObject assemblies = JObject.Parse(assembliesJson);

            return assemblies["runtimeAssemblies"]
                .ToObject<ScriptRuntimeAssembly[]>()
                .ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
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
            IEnumerable<string> fallbacks = GetFallbacks(_ridGraph.Value, rid);

            return new RuntimeFallbacks(rid, fallbacks);
        }
    }
}
