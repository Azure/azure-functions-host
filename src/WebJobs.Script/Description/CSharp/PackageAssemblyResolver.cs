// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Resolves assemblies referenced by NuGet packages used by the function.
    /// </summary>
    public sealed class PackageAssemblyResolver
    {
        private const string EmptyFolderFileMarker = "_._";
        private const string FrameworkTargetName = ".NETFramework,Version=v4.6";

        private readonly IDictionary<AssemblyName, string> _assemblyRegistry;
        
        public PackageAssemblyResolver(FunctionMetadata metadata)
        {
            _assemblyRegistry = InitializeAssemblyRegistry(metadata);
        }

        private static IDictionary<AssemblyName, string> InitializeAssemblyRegistry(FunctionMetadata metadata)
        {
            IDictionary<AssemblyName, string> registry = null;
            string fileName = Path.Combine(Path.GetDirectoryName(metadata.Source), CSharpConstants.ProjectLockFileName);
            
            if (File.Exists(fileName))
            {
                var jobject = JObject.Parse(File.ReadAllText(fileName));

                var target = jobject.SelectTokens(string.Format(CultureInfo.InvariantCulture, "$.targets['{0}']", FrameworkTargetName)).FirstOrDefault();

                if (target != null)
                {
                    string userProfile = Environment.ExpandEnvironmentVariables("%userprofile%");
                    string nugetHome = Path.Combine(userProfile, ".nuget\\packages");

                    List<string> assemblyReferences = new List<string>();
                    List<string> frameworkAssemblyReferences = new List<string>();

                    foreach (JProperty token in target)
                    {
                        var references = token.SelectTokens("$..compile").FirstOrDefault();
                        if (references != null)
                        {
                            foreach (JProperty reference in references)
                            {
                                if (!reference.Name.EndsWith(EmptyFolderFileMarker, StringComparison.Ordinal))
                                {
                                    string path = Path.Combine(nugetHome, token.Name, reference.Name);
                                    path = path.Replace('/', '\\');
                                    assemblyReferences.Add(path);
                                }
                            }
                        }

                        var frameworkAssemblies = token.SelectTokens("$..frameworkAssemblies").FirstOrDefault();
                        if (frameworkAssemblies != null)
                        {
                            foreach (var assembly in frameworkAssemblies)
                            {
                                frameworkAssemblyReferences.Add(assembly.ToString());
                            }
                        }
                    }

                    registry = assemblyReferences.Where(reference => File.Exists(reference)).Union(frameworkAssemblyReferences.Distinct())
                        .ToDictionary(s => Path.IsPathRooted(s) ? AssemblyName.GetAssemblyName(s) : new AssemblyName(s), s => s);
                }
            }

            return registry ?? new Dictionary<AssemblyName, string>();
        }

        public bool TryResolveAssembly(string name, out string path)
        {
            var assemblyName = new AssemblyName(name);
            return _assemblyRegistry.TryGetValue(assemblyName, out path);
        }

        public IDictionary<AssemblyName, string> AssemblyRegistry { get { return _assemblyRegistry; } }
    }
}
