// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// An <see cref="AssemblyLoadContext"/> used to load dynamically compiled functions and their dependencies.
    /// </summary>
    internal sealed partial class DynamicFunctionAssemblyLoadContext : FunctionAssemblyLoadContext
    {
        private readonly FunctionMetadata _functionMetadata;
        private readonly IFunctionMetadataResolver _metadataResolver;
        private readonly ILogger _logger;

        public DynamicFunctionAssemblyLoadContext(FunctionMetadata functionMetadata, IFunctionMetadataResolver resolver, ILogger logger)
            : base(ResolveFunctionAppRoot())
        {
            _functionMetadata = functionMetadata ?? throw new ArgumentNullException(nameof(functionMetadata));
            _metadataResolver = resolver;
            _logger = logger ?? NullLogger.Instance;
        }

        protected override Assembly OnResolvingAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            Assembly assembly = base.OnResolvingAssembly(context, assemblyName);

            if (assembly == null)
            {
                Logger.AssemblyResolutionFailure(_logger, assemblyName.FullName, _functionMetadata.Name);
            }

            return assembly;
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Verify if the shared context contains or can load the assembly first.
            // Unification will always be done to the version in the shared context. 
            Assembly assembly = _metadataResolver?.ResolveAssembly(assemblyName, this);
            if (assembly == null)
            {
                Logger.AssemblyResolved(_logger, assemblyName.FullName, _functionMetadata.Name);
            }

            return assembly;
        }
    }
}
