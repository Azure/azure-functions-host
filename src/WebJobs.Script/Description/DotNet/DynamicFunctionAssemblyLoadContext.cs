// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// An <see cref="AssemblyLoadContext"/> used to load dynamically compiled functions and their dependencies.
    /// </summary>
    internal partial class DynamicFunctionAssemblyLoadContext : FunctionAssemblyLoadContext
    {
        private readonly FunctionMetadata _functionMetadata;
        private readonly IFunctionMetadataResolver _metadataResolver;
        private readonly ILogger _logger;

        public DynamicFunctionAssemblyLoadContext(FunctionMetadata functionMetadata, IFunctionMetadataResolver resolver, ILogger logger)
            : base(ResolveFunctionBaseProbingPath())
        {
            _functionMetadata = functionMetadata ?? throw new ArgumentNullException(nameof(functionMetadata));
            _metadataResolver = resolver;
            _logger = logger ?? NullLogger.Instance;
        }

        protected virtual FunctionAssemblyLoadContext SharedContext => FunctionAssemblyLoadContext.Shared;

        protected override Assembly OnResolvingAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            Assembly assembly = base.OnResolvingAssembly(context, assemblyName);

            if (assembly == null)
            {
                _logger.AssemblyDynamiclyResolutionFailure(assemblyName.FullName, _functionMetadata.Name);
            }

            return assembly;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Verify if the shared context contains or can load the assembly first.
            (bool loadedInSharedContext, Assembly assembly, bool isRuntimeAssembly) = SharedContext.TryLoadAssembly(assemblyName);

            if (isRuntimeAssembly)
            {
                return null;
            }

            if (loadedInSharedContext)
            {
                return assembly;
            }

            assembly = _metadataResolver?.ResolveAssembly(assemblyName, this);
            if (assembly == null)
            {
                _logger.AssemblyDynamiclyResolved(assemblyName.FullName, _functionMetadata.Name);
            }

            return assembly;
        }
    }
}
