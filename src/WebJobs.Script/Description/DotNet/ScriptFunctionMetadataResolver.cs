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
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Provides runtime and compile-time assembly/metadata resolution for compilations using the script model, loading privately deployed
    /// or package assemblies.
    /// </summary>
    public sealed class ScriptFunctionMetadataResolver : MetadataReferenceResolver, IFunctionMetadataResolver
    {
        private readonly string _privateAssembliesPath;
        private readonly string _scriptFileDirectory;
        private readonly string _scriptFilePath;
        private readonly string[] _assemblyExtensions = new[] { ".exe", ".dll" };
        private readonly string _id = Guid.NewGuid().ToString();
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, string> _externalReferences = new ConcurrentDictionary<string, string>();
        private readonly ExtensionSharedAssemblyProvider _extensionSharedAssemblyProvider;

        private PackageAssemblyResolver _packageAssemblyResolver;
        private MetadataReferenceResolver _scriptResolver;

        private static readonly string[] DefaultAssemblyReferences =
           {
                typeof(ILoggerFactory).Assembly.Location, /*Microsoft.Extensions.Logging.Abstractions*/
                typeof(IAsyncCollector<>).Assembly.Location, /*Microsoft.Azure.WebJobs*/
                typeof(JobHost).Assembly.Location, /*Microsoft.Azure.WebJobs.Host*/
                typeof(WebJobs.Extensions.ExtensionsWebJobsStartup).Assembly.Location, /*Microsoft.Azure.WebJobs.Extensions*/
                typeof(AspNetCore.Http.HttpRequest).Assembly.Location, /*Microsoft.AspNetCore.Http.Abstractions*/
                typeof(AspNetCore.Mvc.IActionResult).Assembly.Location, /*Microsoft.AspNetCore.Mvc.Abstractions*/
                typeof(AspNetCore.Mvc.RedirectResult).Assembly.Location, /*Microsoft.AspNetCore.Mvc.Core*/
                typeof(AspNetCore.Http.IQueryCollection).Assembly.Location, /*Microsoft.AspNetCore.Http.Features*/
                typeof(Microsoft.Extensions.Primitives.StringValues).Assembly.Location, /*Microsoft.Extensions.Primitives*/
                typeof(System.Net.Http.HttpClientExtensions).Assembly.Location /*System.Net.Http.Formatting*/
            };

        private static readonly List<ISharedAssemblyProvider> SharedAssemblyProviders = new List<ISharedAssemblyProvider>
            {
                new DirectSharedAssemblyProvider(typeof(Newtonsoft.Json.JsonConvert).Assembly), /* Newtonsoft.Json */
                new DirectSharedAssemblyProvider(typeof(Microsoft.WindowsAzure.Storage.StorageUri).Assembly), /* Microsoft.WindowsAzure.Storage */
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
                "Microsoft.Azure.WebJobs.Host",
                "Microsoft.Extensions.Logging",
                "Microsoft.AspNetCore.Http"
            };

        public ScriptFunctionMetadataResolver(string scriptFilePath, ICollection<IScriptBindingProvider> bindingProviders, ILogger logger)
        {
            _scriptFileDirectory = Path.GetDirectoryName(scriptFilePath);
            _scriptFilePath = scriptFilePath;
            _packageAssemblyResolver = new PackageAssemblyResolver(_scriptFileDirectory);
            _privateAssembliesPath = GetBinDirectory(_scriptFileDirectory);
            var scriptResolver = ScriptMetadataResolver.Default.WithSearchPaths(_privateAssembliesPath);
            _scriptResolver = new CacheMetadataResolver(scriptResolver);
            _extensionSharedAssemblyProvider = new ExtensionSharedAssemblyProvider(bindingProviders);
            _logger = logger ?? NullLogger.Instance;
        }

        public ScriptOptions CreateScriptOptions()
        {
            _externalReferences.Clear();

            return ScriptOptions.Default
                .WithFilePath(Path.GetFileName(_scriptFilePath))
                .WithMetadataResolver(this)
                .WithReferences(GetCompilationReferences())
                .WithImports(DefaultNamespaceImports)
                .WithSourceResolver(new SourceFileResolver(ImmutableArray<string>.Empty, _scriptFileDirectory));
        }

        /// <summary>
        /// Gets the private 'bin' path for a given script.
        /// </summary>
        /// <param name="baseDirectory">The path to the base directory.</param>
        /// <returns>The path to the function's private assembly folder</returns>
        private static string GetBinDirectory(string baseDirectory)
        {
            return Path.Combine(Path.GetFullPath(baseDirectory), DotNetConstants.PrivateAssembliesFolderName);
        }

        public override bool Equals(object other)
        {
            var otherResolver = other as ScriptFunctionMetadataResolver;
            return otherResolver != null && string.Compare(_id, otherResolver._id, StringComparison.Ordinal) == 0;
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public IReadOnlyCollection<string> GetCompilationReferences()
        {
            // Combine our default references with package references
            var combinedReferences = DotNetConstants.FrameworkReferences
                .Union(DefaultAssemblyReferences)
                .Union(_packageAssemblyResolver.AssemblyReferences);

            return new ReadOnlyCollection<string>(combinedReferences.ToList());
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
                p.CompileTimeAssemblies.Keys.Any(a => string.Compare(a.Name, referenceName) == 0));

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

                    if (SharedAssemblyProviders.Any(p => p.TryResolveAssembly(reference, AssemblyLoadContext.Default, _logger, out assembly)) ||
                        _extensionSharedAssemblyProvider.TryResolveAssembly(reference, AssemblyLoadContext.Default, _logger, out assembly))
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

                // If this is a relative assembly reference, use the function script directory as the base probing path
                if (reference.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) > -1)
                {
                    basePath = _scriptFileDirectory;
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

        public Assembly ResolveAssembly(AssemblyName assemblyName, FunctionAssemblyLoadContext targetContext)
        {
            Assembly assembly = null;
            if (_externalReferences.TryGetValue(assemblyName.FullName, out string assemblyPath))
            {
                // For external references, we load the assemblies into the shared context:
                assembly = FunctionAssemblyLoadContext.Shared.LoadFromAssemblyPath(assemblyPath, true);
            }
            else if (TryResolvePrivateAssembly(assemblyName.FullName, out assemblyPath))
            {
                assembly = targetContext.LoadFromStream(new MemoryStream(File.ReadAllBytes(assemblyPath)));
            }
            else if (_packageAssemblyResolver.TryResolveAssembly(assemblyName.FullName, out assemblyPath))
            {
                assembly = targetContext.LoadFromAssemblyPath(assemblyPath);
            }
            else
            {
                _extensionSharedAssemblyProvider.TryResolveAssembly(assemblyName.FullName, FunctionAssemblyLoadContext.Shared, _logger, out assembly);
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

        public async Task<PackageRestoreResult> RestorePackagesAsync()
        {
            var packageManager = new PackageManager(_scriptFileDirectory, _logger);
            PackageRestoreResult result = await packageManager.RestorePackagesAsync();

            // Reload the resolver
            _packageAssemblyResolver = new PackageAssemblyResolver(_scriptFileDirectory);

            return result;
        }

        public bool RequiresPackageRestore(FunctionMetadata metadata)
        {
            return PackageManager.RequiresPackageRestore(Path.GetDirectoryName(metadata.ScriptFile));
        }
    }
}
