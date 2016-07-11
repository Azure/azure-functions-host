// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Provides runtime and compile-time assembly/metadata resolution for a given assembly, loading privately deployed
    /// or package assemblies.
    /// </summary>
    [CLSCompliant(false)]
    public sealed class FunctionMetadataResolver : MetadataReferenceResolver, IFunctionMetadataResolver
    {
        private readonly string _privateAssembliesPath;
        private readonly string[] _assemblyExtensions = new[] { ".exe", ".dll" };
        private readonly string _id = Guid.NewGuid().ToString();
        private readonly FunctionMetadata _functionMetadata;
        private readonly TraceWriter _traceWriter;
        private readonly ConcurrentDictionary<string, string> _externalReferences = new ConcurrentDictionary<string, string>();
        private readonly ExtensionSharedAssemblyProvider _extensionSharedAssemblyProvider;

        private PackageAssemblyResolver _packageAssemblyResolver;
        private ScriptMetadataResolver _scriptResolver;

        private static readonly string[] DefaultAssemblyReferences =
           {
                "System",
                "System.Core",
                "System.Configuration",
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

        private static readonly List<ISharedAssemblyProvider> SharedAssemblyProviders = new List<ISharedAssemblyProvider>
            {
                new DirectSharedAssemblyProvider(typeof(Newtonsoft.Json.JsonConvert).Assembly), /* Newtonsoft.Json */
                new DirectSharedAssemblyProvider(typeof(WindowsAzure.Storage.StorageUri).Assembly), /* Microsoft.WindowsAzure.Storage */
                new LocalSharedAssemblyProvider(@"^Microsoft\.AspNet\.WebHooks\..*"), /* Microsoft.AspNet.WebHooks.* */
            };

        private static readonly string[] DefaultNamespaceImports =
            {
                "System",
                "System.Collections.Generic",
                "System.IO",
                "System.Linq",
                "System.Net.Http",
                "System.Threading.Tasks",
                "Microsoft.Azure.WebJobs",
                "Microsoft.Azure.WebJobs.Host"
            };

        public FunctionMetadataResolver(FunctionMetadata metadata, Collection<ScriptBindingProvider> bindingProviders, TraceWriter traceWriter)
        {
            _functionMetadata = metadata;
            _traceWriter = traceWriter;
            _packageAssemblyResolver = new PackageAssemblyResolver(metadata);
            _privateAssembliesPath = GetBinDirectory(metadata);
            _scriptResolver = ScriptMetadataResolver.Default.WithSearchPaths(_privateAssembliesPath);
            _extensionSharedAssemblyProvider = new ExtensionSharedAssemblyProvider(bindingProviders);
        }

        public ScriptOptions CreateScriptOptions()
        {
            _externalReferences.Clear();

            return ScriptOptions.Default
                    .WithMetadataResolver(this)
                    .WithReferences(GetCompilationReferences())
                    .WithImports(DefaultNamespaceImports)
                    .WithSourceResolver(new SourceFileResolver(ImmutableArray<string>.Empty, Path.GetDirectoryName(_functionMetadata.ScriptFile)));
        }

        /// <summary>
        /// Gets the private 'bin' path for a given script.
        /// </summary>
        /// <param name="metadata">The function metadata.</param>
        /// <returns>The path to the function's private assembly folder</returns>
        private static string GetBinDirectory(FunctionMetadata metadata)
        {
            string functionDirectory = Path.GetDirectoryName(metadata.ScriptFile);
            return Path.Combine(Path.GetFullPath(functionDirectory), DotNetConstants.PrivateAssembliesFolderName);
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
            var result = new List<string>(_packageAssemblyResolver.AssemblyReferences);

            // Add default references
            result.AddRange(DefaultAssemblyReferences);

            return result.AsReadOnly();
        }

        /// <summary>
        /// Attempts to resolve a package reference based on a reference name.
        /// </summary>
        /// <param name="referenceName">The reference name</param>
        /// <param name="package">The package reference, if the <paramref name="referenceName"/>
        /// matches a NuGet package reference name or one of the assemblies referenced by the package; otherwise, null.</param>
        /// <returns>True if a match is found; otherwise, null.</returns>
        public bool TryGetPackageReference(string referenceName, out PackageReference package)
        {
            if (HasValidAssemblyFileExtension(referenceName))
            {
                referenceName = Path.GetFileNameWithoutExtension(referenceName);
            }

            package = _packageAssemblyResolver.Packages.FirstOrDefault(p =>
                string.Compare(referenceName, p.Name, StringComparison.OrdinalIgnoreCase) == 0 ||
                p.Assemblies.Keys.Any(a => string.Compare(a.Name, referenceName) == 0));

            return package != null;
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
                    Assembly assembly = null;

                    if (SharedAssemblyProviders.Any(p => p.TryResolveAssembly(reference, out assembly)) ||
                        _extensionSharedAssemblyProvider.TryResolveAssembly(reference, out assembly))
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
                bool externalReference = false;
                string basePath = _privateAssembliesPath;
                
                // If this is a relative assembly reference, use the function directory as the base probing path
                if (reference.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) > -1)
                {
                    basePath = Path.GetDirectoryName(_functionMetadata.ScriptFile);
                    externalReference = true;
                }

                string filePath = Path.GetFullPath(Path.Combine(basePath, reference));
                if (File.Exists(filePath))
                {
                    if (externalReference)
                    {
                        var assemblyName = AssemblyName.GetAssemblyName(filePath);
                        _externalReferences.TryAdd(assemblyName.FullName, filePath);
                    }

                    return ImmutableArray.Create(MetadataReference.CreateFromFile(filePath));
                }
            }

            return ImmutableArray<PortableExecutableReference>.Empty;
        }

        public Assembly ResolveAssembly(string assemblyName)
        {
            Assembly assembly = null;
            string assemblyPath = null;

            if (_externalReferences.TryGetValue(assemblyName, out assemblyPath))
            {
                // When loading shared assemblies, load into the load-from context and load assembly dependencies
                assembly = Assembly.LoadFrom(assemblyPath);
            }
            else if (TryResolvePrivateAssembly(assemblyName, out assemblyPath) ||
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
