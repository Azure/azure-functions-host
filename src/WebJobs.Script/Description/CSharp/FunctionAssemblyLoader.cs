// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Manages runtime assembly resolution for managed code functions, 
    /// loading assemblies from their respective <see cref="FunctionAssemblyLoadContext"/>. 
    /// </summary>
    public class FunctionAssemblyLoader : IDisposable
    {
        private readonly List<FunctionAssemblyLoadContext> _functionContexts = new List<FunctionAssemblyLoadContext>();

        public FunctionAssemblyLoader()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
            }
        }

        private Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            FunctionAssemblyLoadContext context = GetFunctionContext(args.RequestingAssembly);
            Assembly result = null;

            if (context != null)
            {
                result = context.ResolveAssembly(args.Name);
            }

            return result;
        }

        [CLSCompliant(false)]
        public FunctionAssemblyLoadContext CreateContext(FunctionMetadata metadata, Assembly functionAssembly, FunctionMetadataResolver metadataResolver)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            var currentContext = GetFunctionContext(metadata);

            if (currentContext != null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Assembly load context for function '{0}' already exists.", metadata.Name));
            }

            var context = new FunctionAssemblyLoadContext(metadata, functionAssembly, metadataResolver);
            _functionContexts.Add(context);

            return context;
        }

        public bool ReleaseContext(FunctionMetadata metadata)
        {
            var context = GetFunctionContext(metadata);

            if (context != null)
            {
                return _functionContexts.Remove(context);
            }

            return false;
        }

        public bool ReleaseContext(Assembly assembly)
        {
            var context = GetFunctionContext(assembly);

            if (context != null)
            {
                return _functionContexts.Remove(context);
            }

            return false;
        }

        private FunctionAssemblyLoadContext GetFunctionContext(Assembly requestingAssembly)
        { 
            return _functionContexts.FirstOrDefault(c => c.FunctionAssembly == requestingAssembly);
        }

        private FunctionAssemblyLoadContext GetFunctionContext(FunctionMetadata metadata)
        {
            return _functionContexts.FirstOrDefault(c => string.Compare(c.Metadata.Name, metadata.Name, StringComparison.Ordinal) == 0);
        }
    }
}
