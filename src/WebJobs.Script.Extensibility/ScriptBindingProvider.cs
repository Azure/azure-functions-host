// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensibility
{
    /// <summary>
    /// Base class for providers of <see cref="ScriptBinding"/>s.
    /// </summary>
    public abstract class ScriptBindingProvider
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="config">The <see cref="JobHostConfiguration"/>.</param>
        /// <param name="hostMetadata">The host configuration metadata.</param>
        /// <param name="traceWriter">The <see cref="TraceWriter"/> that can be used to log trace events.</param>
        protected ScriptBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter)
        {
            Config = config;
            Metadata = hostMetadata;
            TraceWriter = traceWriter;
        }

        /// <summary>
        /// Gets the <see cref="JobHostConfiguration"/>.
        /// </summary>
        protected JobHostConfiguration Config { get; private set; }

        /// <summary>
        /// Gets the host configuration metadata.
        /// </summary>
        protected JObject Metadata { get; private set; }

        /// <summary>
        /// Gets the <see cref="TraceWriter"/> that can be used to log trace events.
        /// </summary>
        protected TraceWriter TraceWriter { get; private set; }

        /// <summary>
        /// Called early in the host initialization pipeline, before bindings have been created
        /// to allow the provider to perform host level initialization, extension registration, etc.
        /// </summary>
        public virtual void Initialize()
        {
        }

        /// <summary>
        /// Create a <see cref="ScriptBinding"/> for the specified metadata if
        /// </summary>
        /// <param name="context">The binding context.</param>
        public abstract bool TryCreate(ScriptBindingContext context, out ScriptBinding binding);

        /// <summary>
        /// Attempt to resolve the specified reference assembly.
        /// </summary>
        /// <remarks>
        /// This allows an extension to support "built in" assemblies for .NET functions so
        /// user code can easily reference them.
        /// </remarks>
        /// <param name="assemblyName">The name of the assembly to resolve.</param>
        /// <param name="assembly">The assembly if we were able to resolve.</param>
        /// <returns>True if the assembly could be resolved, false otherwise.</returns>
        public virtual bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            assembly = null;
            return false;
        }
    }
}
