// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public FunctionAssemblyLoadContext(FunctionMetadata functionMetadata, Assembly functionAssembly, FunctionMetadataResolver resolver)
        {
            _metadataResolver = resolver;
            FunctionAssembly = functionAssembly;
            Metadata = functionMetadata;
        }

        public Assembly FunctionAssembly { get; }

        public FunctionMetadata Metadata { get; }

        internal Assembly ResolveAssembly(string name) 
            => _metadataResolver.ResolveAssembly(name);
    }
}
