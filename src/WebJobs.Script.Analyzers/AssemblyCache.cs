// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.Functions.Analyzers
{
    // Only support extensions and WebJobs core.
    // Although extensions may refer to other dlls.
    public class AssemblyCache
    {
        // Map from assembly identities to full paths
        public static AssemblyCache Instance = new AssemblyCache();

        bool _registered;

        // Assembly Display Name --> Path
        Dictionary<string, string> _assemblyNameToPathMap = new Dictionary<string, string>();

        // Assembly Display Name --> loaded Assembly object
        Dictionary<string, Assembly> _assemblyNameToObjMap = new Dictionary<string, Assembly>();

        const string WebJobsAssemblyName = "Microsoft.Azure.WebJobs";
        const string WebJobsHostAssemblyName = "Microsoft.Azure.WebJobs.Host";

        JobHostMetadataProvider _tooling;

        internal JobHostMetadataProvider Tooling => _tooling;
        private int _projectCount;

        // $$$ This can get invoked multiple times concurrently
        // This will get called on every compilation.
        // So return early on subsequent initializations.
        internal void Build(Compilation compilation)
        {
            Register();

            int count;
            lock (this)
            {
                // If project references have changed, then reanalyze to pick up new dependencies.
                var refs = compilation.References.OfType<PortableExecutableReference>().ToArray();
                count = refs.Length;
                if ((count == _projectCount) && (_tooling != null))
                {
                    return; // already initialized.
                }

                // Even for netStandard/.core projects, this will still be a flattened list of the full transitive closure of dependencies.
                foreach (var asm in compilation.References.OfType<PortableExecutableReference>())
                {
                    var dispName = asm.Display; // For .net core, the displayname can be the full path
                    var path = asm.FilePath;

                    _assemblyNameToPathMap[dispName] = path;
                }

                // Builtins
                _assemblyNameToObjMap["mscorlib"] = typeof(object).Assembly;
                _assemblyNameToObjMap[WebJobsAssemblyName] = typeof(Microsoft.Azure.WebJobs.FunctionNameAttribute).Assembly;
                _assemblyNameToObjMap[WebJobsHostAssemblyName] = typeof(Microsoft.Azure.WebJobs.JobHost).Assembly;

                // JSON.Net?
            }

            // Produce tooling object
            var webjobsStartups = new List<Type>();
            foreach (var path in _assemblyNameToPathMap.Values)
            {
                // We don't want to load and reflect over every dll.
                // By convention, restrict based on filenames.
                var filename = Path.GetFileName(path);
                // TODO: Can this search be narrowed, e.g. "webjobs.extensions"?
                if (!filename.ToLowerInvariant().Contains("extension"))
                {
                    continue;
                }
                if (path.Contains(@"\ref\"))    // Skip reference assemblies.
                {
                    continue;
                }

                Assembly assembly;
                try
                {
                    // See GetNuGetPackagesPath for details
                    // Script runtime is already setup with assembly resolution hooks, so use LoadFrom
                    assembly = Assembly.LoadFrom(path);

                    string asmName = new AssemblyName(assembly.FullName).Name;
                    _assemblyNameToObjMap[asmName] = assembly;

                    var test = assembly.GetCustomAttributes<WebJobsStartupAttribute>().Select(a => a.WebJobsStartupType);
                    if (test.Count() > 0)
                    {
                        webjobsStartups.AddRange(test);
                    }
                }
                catch (Exception e)
                {
                    // Could be a reference assembly.
                    continue;
                }
            }

            var host = new HostBuilder()
                .ConfigureWebJobs(b =>
                {
                    b.AddAzureStorageCoreServices()
                        .UseExternalStartup(new CompilationWebJobsStartupTypeLocator(_assemblyNameToObjMap.Values.ToArray()));
                })
                .Build();
            var tooling = (JobHostMetadataProvider)host.Services.GetRequiredService<IJobHostMetadataProvider>();

            lock (this)
            {
                this._projectCount = count;
                this._tooling = tooling;
            }
        }

        public bool TryMapAssembly(IAssemblySymbol asm, out Assembly asmRef)
        {
            // Top-level map only supports mscorlib, webjobs, or extensions
            var asmName = asm.Identity.Name;

            Assembly asm2;
            if (_assemblyNameToObjMap.TryGetValue(asmName, out asm2))
            {
                asmRef = asm2;
                return true;
            }

            // Is this an extension? Must have a reference to WebJobs
            bool isWebJobsAssembly = false;
            foreach (var module in asm.Modules)
            {
                foreach (var asmReference in module.ReferencedAssemblies)
                {
                    if (asmReference.Name == WebJobsAssemblyName)
                    {
                        isWebJobsAssembly = true;
                        goto Done;
                    }
                }
            }
        Done:
            if (!isWebJobsAssembly)
            {
                asmRef = null;
                return false;
            }

            foreach (var kv in _assemblyNameToPathMap)
            {
                var path = kv.Value;
                var shortName = Path.GetFileNameWithoutExtension(path);

                if (string.Equals(asmName, shortName, StringComparison.OrdinalIgnoreCase))
                {
                    var asm3 = Assembly.LoadFile(path);
                    _assemblyNameToObjMap[asmName] = asm3;

                    asmRef = asm3;
                    return true;
                }
            }

            throw new NotImplementedException();
        }

        public void Register()
        {
            if (_registered)
            {
                return;
            }
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            _registered = true;
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var an = new AssemblyName(args.Name);
            var context = args.RequestingAssembly;

            Assembly asm2;
            if (_assemblyNameToObjMap.TryGetValue(an.Name, out asm2))
            {
                return asm2;
            }

            asm2 = LoadFromProjectReference(an);
            if (asm2 != null)
            {
                _assemblyNameToObjMap[an.Name] = asm2;
            }

            return asm2;
        }

        private Assembly LoadFromProjectReference(AssemblyName an)
        {
            foreach (var kv in _assemblyNameToPathMap)
            {
                var path = kv.Key;
                if (path.Contains(@"\ref\")) // Skip reference assemblies.
                {
                    continue;
                }

                var filename = Path.GetFileNameWithoutExtension(path);

                // Simplifying assumption: assume dll name matches assembly name.
                // Use this as a filter to limit the number of file-touches.
                if (string.Equals(filename, an.Name, StringComparison.OrdinalIgnoreCase))
                {
                    var an2 = AssemblyName.GetAssemblyName(path);

                    if (string.Equals(an2.FullName, an.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        var a = Assembly.LoadFrom(path);
                        return a;
                    }
                }
            }
            return null;
        }
    }
}