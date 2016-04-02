// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Manages runtime assembly resolution for managed code functions, 
    /// loading assemblies from their respective <see cref="FunctionAssemblyLoadContext"/>. 
    /// </summary>
    public class FunctionAssemblyLoader : IDisposable
    {
        // Prefix that uniquely identifies our assemblies
        // i.e.: "ƒ-<functionname>"
        public const string AssemblyPrefix = "\u0192-";

        private readonly ConcurrentDictionary<string, FunctionAssemblyLoadContext> _functionContexts = new ConcurrentDictionary<string, FunctionAssemblyLoadContext>();
        private readonly Regex _functionNameFromAssemblyRegex;
        private readonly Uri _rootScriptUri;

        public FunctionAssemblyLoader(string rootScriptPath)
        {
            _rootScriptUri = new Uri(rootScriptPath, UriKind.RelativeOrAbsolute);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            _functionNameFromAssemblyRegex = new Regex(string.Format(CultureInfo.InvariantCulture, "^{0}(?<name>.*?)#", AssemblyPrefix), RegexOptions.Compiled);
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
        public FunctionAssemblyLoadContext CreateOrUpdateContext(FunctionMetadata metadata, Assembly functionAssembly, FunctionMetadataResolver metadataResolver)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException("metadata");
            }
            if (functionAssembly == null)
            {
                throw new ArgumentNullException("functionAssembly");
            }
            if (metadataResolver == null)
            {
                throw new ArgumentNullException("metadataResolver");
            }

            var context = new FunctionAssemblyLoadContext(metadata, functionAssembly, metadataResolver);
            
            return _functionContexts.AddOrUpdate(metadata.Name, context, (s, o) => context);
        }

        public bool ReleaseContext(FunctionMetadata metadata)
        {
            FunctionAssemblyLoadContext context;
            return _functionContexts.TryRemove(metadata.Name, out context);
        }

        private FunctionAssemblyLoadContext GetFunctionContext(Assembly requestingAssembly)
        { 
            if (requestingAssembly == null)
            {
                return null;
            }

            FunctionAssemblyLoadContext context = null;
            string functionName = GetFunctionNameFromAssembly(requestingAssembly);
            if (functionName != null)
            {
                context = GetFunctionContext(functionName);

                // If the context is for a different assembly 
                if (context != null && context.FunctionAssembly != requestingAssembly)
                {
                    return null;
                }
            }
            else
            {
                context = GetFunctionContextFromDependency(requestingAssembly);
            }

            return context;
        }

        private FunctionAssemblyLoadContext GetFunctionContextFromDependency(Assembly requestingAssembly)
        {
            // If this is a private reference, get the context based on the CodeBase
            if (Uri.IsWellFormedUriString(requestingAssembly.CodeBase, UriKind.RelativeOrAbsolute))
            {
                var codebaseUri = new Uri(requestingAssembly.CodeBase, UriKind.RelativeOrAbsolute);

                if (_rootScriptUri.IsBaseOf(codebaseUri))
                {
                    return _functionContexts.Values.FirstOrDefault(c => c.FunctionBaseUri.IsBaseOf(codebaseUri));
                }
            }

            return _functionContexts.Values.FirstOrDefault(c => c.LoadedAssemblies.Any(a => a == requestingAssembly));
        }

        private FunctionAssemblyLoadContext GetFunctionContext(string functionName)
        {
            FunctionAssemblyLoadContext context;
            _functionContexts.TryGetValue(functionName, out context);

            return context;
        }

        public static string GetAssemblyNameFromMetadata(FunctionMetadata metadata, string suffix)
        {
            return AssemblyPrefix + metadata.Name + "#" + suffix;
        }

        public string GetFunctionNameFromAssembly(Assembly assembly)
        {
            if (assembly != null)
            {
                Match match = _functionNameFromAssemblyRegex.Match(assembly.FullName);

                if (match.Success)
                {
                    return match.Groups["name"].Value;
                }
            }

            return null;
        }
    }   
}
