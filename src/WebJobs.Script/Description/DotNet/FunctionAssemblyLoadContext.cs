// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Establishes an assembly load context for a given function.
    /// Assemblies loaded from this context are loaded using the <see cref="IFunctionMetadataResolver"/> associated
    /// with a given function.
    /// </summary>
    public sealed class FunctionAssemblyLoadContext
    {
        private readonly IFunctionMetadataResolver _metadataResolver;
        private readonly TraceWriter _traceWriter;
        private readonly ILogger _logger;
        private ImmutableArray<Assembly> _loadedAssemblies;
        private Uri _functionBaseUri;

        public FunctionAssemblyLoadContext(FunctionMetadata functionMetadata, Assembly functionAssembly, IFunctionMetadataResolver resolver, TraceWriter traceWriter, ILogger logger)
        {
            _metadataResolver = resolver;
            _loadedAssemblies = ImmutableArray<Assembly>.Empty;
            _traceWriter = traceWriter;
            _logger = logger;
            FunctionAssembly = functionAssembly;
            Metadata = functionMetadata;
        }

        public ImmutableArray<Assembly> LoadedAssemblies => _loadedAssemblies;

        public TraceWriter TraceWriter => _traceWriter;

        public ILogger Logger => _logger;

        public Uri FunctionBaseUri
        {
            get
            {
                if (_functionBaseUri == null)
                {
                    _functionBaseUri = new Uri(Path.GetDirectoryName(Metadata.ScriptFile) + Path.DirectorySeparatorChar, UriKind.RelativeOrAbsolute);
                }

                return _functionBaseUri;
            }
        }

        public Assembly FunctionAssembly { get; private set; }

        public FunctionMetadata Metadata { get; private set; }

        internal Assembly ResolveAssembly(string name)
        {
            Assembly assembly = _metadataResolver.ResolveAssembly(name);

            if (assembly != null)
            {
                _loadedAssemblies = _loadedAssemblies.Add(assembly);
            }

            return assembly;
        }
    }
}
