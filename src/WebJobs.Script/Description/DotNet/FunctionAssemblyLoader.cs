// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Manages runtime assembly resolution for managed code functions,
    /// loading assemblies from their respective <see cref="FunctionAssemblyLoadContext"/>.
    /// </summary>
    public class FunctionAssemblyLoader : IDisposable
    {
        // Prefix that uniquely identifies our assemblies
        // i.e.: "f-<functionname>"
        public const string AssemblyPrefix = "f-";
        public const string AssemblySeparator = "__";

        private readonly ConcurrentDictionary<string, FunctionAssemblyLoadContext> _functionContexts = new ConcurrentDictionary<string, FunctionAssemblyLoadContext>();
        private readonly Regex _functionNameFromAssemblyRegex;
        private readonly Uri _rootScriptUri;

        public FunctionAssemblyLoader(string rootScriptPath)
        {
            _rootScriptUri = new Uri(rootScriptPath, UriKind.RelativeOrAbsolute);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            _functionNameFromAssemblyRegex = new Regex(string.Format(CultureInfo.InvariantCulture, "^{0}(?<name>.*?){1}", AssemblyPrefix, AssemblySeparator), RegexOptions.Compiled);
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

        internal Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            FunctionAssemblyLoadContext context = GetFunctionContext(args.RequestingAssembly);
            Assembly result = null;

            try
            {
                if (context != null)
                {
                    result = context.ResolveAssembly(args.Name);
                }

                // If we were unable to resolve the assembly, apply the current App Domain policy and attempt to load it.
                // This allows us to correctly handle retargetable assemblies, redirects, etc.
                if (result == null)
                {
                    string assemblyName = ((AppDomain)sender).ApplyPolicy(args.Name);

                    // If after applying the current policy, we now have a different target assembly name, attempt to load that
                    // assembly
                    if (string.Compare(assemblyName, args.Name) != 0)
                    {
                        result = Assembly.Load(assemblyName);
                    }
                }
            }
            catch (Exception e)
            {
                if (context != null)
                {
                    string message = string.Format(CultureInfo.InvariantCulture,
                        "Exception during runtime resolution of assembly '{0}': '{1}'", args.Name, e.ToString());
                    context.TraceWriter.Warning(message);
                    context.Logger?.LogWarning(message);
                }
            }

            // If we have an function context and failed to resolve a function assembly dependency,
            // log the failure as this is usually caused by missing private assemblies.
            if (context != null && result == null)
            {
                string message = string.Format(CultureInfo.InvariantCulture,
                    "Unable to find assembly '{0}'. Are you missing a private assembly file?", args.Name);
                context.TraceWriter.Warning(message);
                context.Logger?.LogWarning(message);
            }

            return result;
        }

        public FunctionAssemblyLoadContext CreateOrUpdateContext(FunctionMetadata metadata, Assembly functionAssembly, IFunctionMetadataResolver metadataResolver,
            TraceWriter traceWriter, ILoggerFactory loggerFactory)
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
            if (traceWriter == null)
            {
                throw new ArgumentNullException("traceWriter");
            }

            ILogger logger = loggerFactory?.CreateLogger(LogCategories.Startup);
            var context = new FunctionAssemblyLoadContext(metadata, functionAssembly, metadataResolver, traceWriter, logger);

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
            string assemblyCodeBase = requestingAssembly.GetCodeBase();
            if (Uri.IsWellFormedUriString(assemblyCodeBase, UriKind.RelativeOrAbsolute))
            {
                var codebaseUri = new Uri(assemblyCodeBase, UriKind.RelativeOrAbsolute);

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
            return AssemblyPrefix + metadata.Name + AssemblySeparator + suffix.GetHashCode().ToString();
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