// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;
using NuGet.ProjectModel;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Resolves assemblies referenced by NuGet packages used by the function.
    /// </summary>
    public sealed class PackageAssemblyResolver
    {
        private const string EmptyFolderFileMarker = "_._";

        private readonly ImmutableArray<PackageReference> _packages;

        public PackageAssemblyResolver(string workingDirectory)
        {
            _packages = InitializeAssemblyRegistry(workingDirectory);
        }

        public ImmutableArray<PackageReference> Packages
        {
            get
            {
                return _packages;
            }
        }

        public IEnumerable<string> AssemblyReferences
        {
            get
            {
                return _packages.Aggregate(new List<string>(), (assemblies, p) =>
                {
                    assemblies.AddRange(p.CompileTimeAssemblies.Values);
                    assemblies.AddRange(p.FrameworkAssemblies.Values);
                    return assemblies;
                }).Distinct();
            }
        }

        private static ImmutableArray<PackageReference> InitializeAssemblyRegistry(string functionDirectory)
        {
            var builder = ImmutableArray<PackageReference>.Empty.ToBuilder();
            string fileName = Path.Combine(functionDirectory, DotNetConstants.ProjectLockFileName);

            if (File.Exists(fileName))
            {
                LockFile lockFile = null;
                try
                {
                    var reader = new LockFileFormat();
                    lockFile = reader.Read(fileName);

                    var target = lockFile.Targets
                        .FirstOrDefault(t => string.Equals(t.TargetFramework.DotNetFrameworkName, FrameworkConstants.CommonFrameworks.NetStandard20.DotNetFrameworkName));

                    if (target != null)
                    {
                        string nugetCachePath = PackageManager.GetNugetPackagesPath();

                        // Get all the referenced libraries for the target, excluding the framework references we add by default
                        var libraries = target.Libraries.Where(s => !DotNetConstants.FrameworkReferences.Contains(s.Name));
                        foreach (var library in libraries)
                        {
                            var package = new PackageReference(library.Name, library.Version.ToFullString());

                            var assemblies = GetAssembliesList(library.CompileTimeAssemblies, nugetCachePath, package);

                            package.CompileTimeAssemblies.AddRange(assemblies);

                            assemblies = GetAssembliesList(library.RuntimeAssemblies, nugetCachePath, package);

                            package.RuntimeAssemblies.AddRange(assemblies);

                            var frameworkAssemblies = library.FrameworkAssemblies.ToDictionary(a => new AssemblyName(a));
                            package.FrameworkAssemblies.AddRange(frameworkAssemblies);

                            builder.Add(package);
                        }
                    }
                }
                catch (FileFormatException)
                {
                    return ImmutableArray<PackageReference>.Empty;
                }
            }

            return builder.ToImmutableArray();
        }

        private static IReadOnlyDictionary<AssemblyName, string> GetAssembliesList(IList<LockFileItem> compileTimeAssemblies, string nugetCachePath, PackageReference package)
        {
            string packagePath = Path.Combine(nugetCachePath, package.Name, package.Version.ToString());
            return compileTimeAssemblies.Select(i => Path.Combine(packagePath, i.Path))
                                .Where(p => !p.EndsWith(EmptyFolderFileMarker) && File.Exists(p))
                                .ToDictionary(p => AssemblyName.GetAssemblyName(p), p => new Uri(p).LocalPath);
        }

        public bool TryResolveAssembly(string name, out string path)
        {
            path = null;
            var assemblyName = new AssemblyName(name);

            foreach (var package in _packages)
            {
                if (package.RuntimeAssemblies.TryGetValue(assemblyName, out path) ||
                    package.FrameworkAssemblies.TryGetValue(assemblyName, out path))
                {
                    break;
                }
            }

            return path != null;
        }
    }
}
