﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Establishes an assembly load context for a given function.
    /// Assemblies loaded from this context are loaded using the <see cref="FunctionMetadataResolver"/> associated
    /// with a given function.
    /// </summary>
    [CLSCompliant(false)]
    public sealed class FunctionAssemblyLoadContext
    {
        private readonly FunctionMetadataResolver _metadataResolver;
        private ImmutableArray<Assembly> _loadedAssemblies;
        private Uri _functionBaseUri;

        public FunctionAssemblyLoadContext(FunctionMetadata functionMetadata, Assembly functionAssembly, FunctionMetadataResolver resolver)
        {
            _metadataResolver = resolver;
            _loadedAssemblies = ImmutableArray<Assembly>.Empty;
            FunctionAssembly = functionAssembly;
            Metadata = functionMetadata;
        }

        public ImmutableArray<Assembly> LoadedAssemblies
        {
            get
            {
                return _loadedAssemblies;
            }
        }

        public Uri FunctionBaseUri
        {
            get
            {
                if (_functionBaseUri == null)
                {
                    _functionBaseUri = new Uri(Path.GetDirectoryName(Metadata.Source), UriKind.RelativeOrAbsolute);
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
