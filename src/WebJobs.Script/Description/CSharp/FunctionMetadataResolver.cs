// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Provides runtime and compile-time assembly/metadata resolution for a given assembly, loading privately deployed
    /// or package assemblies.
    /// </summary>
    [CLSCompliant(false)]
    public sealed class FunctionMetadataResolver : MetadataReferenceResolver
    {
        private readonly string _privateAssembliesPath;
        private readonly string[] _assemblyExtensions = new[] { ".exe", ".dll" };
        private readonly string _id = Guid.NewGuid().ToString();
        private readonly FunctionMetadata _functionMetadata;
        private readonly TraceWriter _traceWriter;

        private PackageAssemblyResolver _packageAssemblyResolver;
        private ScriptMetadataResolver _scriptResolver;

        private static readonly string[] DefaultAssemblyReferences =
           {
                "System",
                "System.Core",
                "System.Xml",
                "System.Net.Http",
                "Microsoft.CSharp",
                typeof(object).Assembly.Location,
                typeof(IAsyncCollector<>).Assembly.Location, /*Microsoft.Azure.WebJobs*/
                typeof(JobHost).Assembly.Location, /*Microsoft.Azure.WebJobs.Host*/
                typeof(CoreJobHostConfigurationExtensions).Assembly.Location, /*Microsoft.Azure.WebJobs.Extensions*/
                typeof(System.Web.Http.ApiController).Assembly.Location, /*System.Web.Http*/
                typeof(System.Net.Http.HttpClientExtensions).Assembly.Location /*System.Net.Http.Formatting*/
            };

        private static readonly Assembly[] SharedAssemblies =
            {
                typeof(Newtonsoft.Json.JsonConvert).Assembly /*Newtonsoft.Json*/
            };

        private static readonly string[] DefaultNamespaceImports =
            {
                "System",
                "System.Collections.Generic",
                "System.Linq",
                "System.Net.Http",
                "Microsoft.Azure.WebJobs",
                "Microsoft.Azure.WebJobs.Host"
            };

        public FunctionMetadataResolver(FunctionMetadata metadata, TraceWriter traceWriter)
        {
            _functionMetadata = metadata;
            _traceWriter = traceWriter;
            _packageAssemblyResolver = new PackageAssemblyResolver(metadata);
            _privateAssembliesPath = GetBinDirectory(metadata);
            _scriptResolver = ScriptMetadataResolver.Default.WithSearchPaths(_privateAssembliesPath);
        }

        public ScriptOptions FunctionScriptOptions
        {
            get
            {
                return ScriptOptions.Default
                        .WithMetadataResolver(this)
                        .WithReferences(GetCompilationReferences())
                        .WithImports(DefaultNamespaceImports);
            }
        }

        /// <summary>
        /// Gets the private 'bin' path for a given script.
        /// </summary>
        /// <param name="metadata">The function metadata.</param>
        /// <returns>The path to the function's private assembly folder</returns>
        private static string GetBinDirectory(FunctionMetadata metadata)
        {
            string functionDirectory = Path.GetDirectoryName(metadata.Source);
            return Path.Combine(Path.GetFullPath(functionDirectory), CSharpConstants.PrivateAssembliesFolderName);
        }

        public override bool Equals(object other)
        {
            var otherResolver = other as FunctionMetadataResolver;
            return otherResolver != null && string.Compare(_id, otherResolver._id, StringComparison.Ordinal) == 0;
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public IReadOnlyCollection<string> GetCompilationReferences()
        {
            // Add package reference assemblies
            var result = new List<string>(_packageAssemblyResolver.AssemblyRegistry.Values);

            // Add default references
            result.AddRange(DefaultAssemblyReferences);

            return result.AsReadOnly();
        }

        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
        {
            if (reference == null)
            {
                return ImmutableArray<PortableExecutableReference>.Empty;
            }

            if (!HasValidAssemblyFileExtension(reference))
            {
                // Try to resolve using the default resolver (framework assemblies, e.g. System.Core, System.Xml, etc.)
                ImmutableArray<PortableExecutableReference> result = _scriptResolver.ResolveReference(reference, baseFilePath, properties);

                // If the default script resolver can't resolve the assembly
                // check if this is one of host's shared assemblies
                if (result.IsEmpty)
                {
                    Assembly assembly = SharedAssemblies
                        .FirstOrDefault(m => string.Compare(m.GetName().Name, reference, StringComparison.OrdinalIgnoreCase) == 0);

                    if (assembly != null)
                    {
                        result = ImmutableArray.Create(MetadataReference.CreateFromFile(assembly.Location));
                    }
                }

                return result;
            }

            return GetMetadataFromReferencePath(reference);
        }

        private ImmutableArray<PortableExecutableReference> GetMetadataFromReferencePath(string reference)
        {
            if (Path.IsPathRooted(reference))
            {
                // If the path is rooted, create a direct reference to the assembly file
                return ImmutableArray.Create(MetadataReference.CreateFromFile(reference));
            }
            else
            {
                // Treat the reference as a private assembly reference
                string filePath = Path.Combine(_privateAssembliesPath, reference);
                if (File.Exists(Path.Combine(filePath)))
                {
                    return ImmutableArray.Create(MetadataReference.CreateFromFile(filePath));
                }
            }

            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        public Assembly ResolveAssembly(string assemblyName)
        {
            Assembly assembly = null;
            string assemblyPath = null;
            if (TryResolvePrivateAssembly(assemblyName, out assemblyPath) ||
                _packageAssemblyResolver.TryResolveAssembly(assemblyName, out assemblyPath))
            {
                // Use LoadFile here to load into the correct context
                assembly = Assembly.LoadFile(assemblyPath);
            }

            return assembly;
        }

        private bool HasValidAssemblyFileExtension(string reference)
        {
            return _assemblyExtensions.Contains(Path.GetExtension(reference));
        }

        private bool TryResolvePrivateAssembly(string name, out string assemblyPath)
        {
            var names = GetProbingFilePaths(name);
            assemblyPath = names.FirstOrDefault(n => File.Exists(n));

            return assemblyPath != null;
        }

        private IEnumerable<string> GetProbingFilePaths(string name)
        {
            var assemblyName = new AssemblyName(name);
            return _assemblyExtensions.Select(ext => Path.Combine(_privateAssembliesPath, assemblyName.Name + ext));
        }

        public async Task RestorePackagesAsync()
        {
            var packageManager = new PackageManager(_functionMetadata, _traceWriter);
            await packageManager.RestorePackagesAsync();

            // Reload the resolver
            _packageAssemblyResolver = new PackageAssemblyResolver(_functionMetadata);
        }
    }
}
