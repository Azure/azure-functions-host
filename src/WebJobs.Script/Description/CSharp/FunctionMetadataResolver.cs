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
    public sealed class FunctionMetadataResolver : MetadataReferenceResolver
    {
        private readonly string _privateAssembliesPath;
        private PackageAssemblyResolver _packageAssemblyResolver;
        private ScriptMetadataResolver _scriptResolver;
        private readonly string[] _assemblyExtensions = new[] { ".exe", ".dll" };
        private readonly string _id = Guid.NewGuid().ToString();
        private readonly FunctionMetadata _functionMetadata;
        private readonly TraceWriter _traceWriter;

        private static readonly string[] _defaultAssemblyReferences =
           {
                "System",
                "System.Core",
                "System.Xml",
                "System.Net.Http",
                typeof(object).Assembly.Location,
                typeof(TraceWriter).Assembly.Location,
                typeof(TimerInfo).Assembly.Location,
                typeof(System.Web.Http.ApiController).Assembly.Location
            };

        private static readonly string[] _defaultNamespaceImports =
            {
                "System",
                "System.Collections.Generic",
                "System.Linq",
                "System.Net.Http"
            };

        public FunctionMetadataResolver(FunctionMetadata metadata, TraceWriter traceWriter)
        {
            _functionMetadata = metadata;
            _traceWriter = traceWriter;
            _packageAssemblyResolver = new PackageAssemblyResolver(metadata, traceWriter);
            _privateAssembliesPath = GetBinDirectory(metadata);
            _scriptResolver = ScriptMetadataResolver.Default.WithSearchPaths(_privateAssembliesPath);
        }

        public ScriptOptions FunctionScriptOptions
            => ScriptOptions.Default
                    .WithMetadataResolver(this)
                    .WithReferences(GetCompilationReferences())
                    .WithImports(_defaultNamespaceImports);

        /// <summary>
        /// Gets the private 'bin' path for a given script.
        /// </summary>
        /// <param name="metadata">The function metadata.</param>
        /// <returns>The path to the function's private assembly folder</returns>
        private string GetBinDirectory(FunctionMetadata metadata)
        {
            string functionDirectory = Path.GetDirectoryName(metadata.Source);
            return Path.Combine(Path.GetFullPath(functionDirectory), CSharpConstants.PrivateAssembliesFolderName);
        }

        public override bool Equals(object other)
        {
            var otherResolver = other as FunctionMetadataResolver;
            return otherResolver != null && string.Compare(_id, otherResolver._id) == 0;
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public List<string> GetCompilationReferences()
        {
            // Add package reference assemblies
            var result = new List<string>(_packageAssemblyResolver.AssemblyRegistry.Values);

            // Add default references
            result.AddRange(_defaultAssemblyReferences);

            return result;
        }

        public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
        {
            if (!HasValidAssemblyFileExtension(reference))
            {
                var result = _scriptResolver.ResolveReference(reference, baseFilePath, properties);

                return result;
            }

            if (Path.IsPathRooted(reference))
            {
                return ImmutableArray.Create(MetadataReference.CreateFromFile(reference));
            }
            else if (reference.StartsWith($"{CSharpConstants.PrivateAssembliesFolderName}\\"))
            {
                string filePath = Path.Combine(_privateAssembliesPath, Path.GetFileName(reference));
                if (File.Exists(Path.Combine(filePath)))
                {
                    Assembly assembly = Assembly.LoadFile(filePath);
                    
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

            yield return Path.Combine(_privateAssembliesPath, $"{assemblyName.Name}.dll");
            yield return Path.Combine(_privateAssembliesPath, $"{assemblyName.Name}.exe");
        }

        public async Task RestorePackagesAsync()
        {
            var packageManager = new PackageManager(_functionMetadata, _traceWriter);
            await packageManager.RestorePackagesAsync();

            // Reload the resolver
            _packageAssemblyResolver = new PackageAssemblyResolver(_functionMetadata, _traceWriter);
        }
    }
}
