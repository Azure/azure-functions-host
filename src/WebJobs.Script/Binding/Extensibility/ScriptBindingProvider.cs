﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensibility
{
    /// <summary>
    /// Base class for providers of <see cref="ScriptBinding"/>s.
    /// </summary>
    public abstract class ScriptBindingProvider
    {
        private readonly IOptions<JobHostOptions> _hostOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptBindingProvider"/> class.
        /// </summary>
        /// <param name="hostOptions">The <see cref="JobHostConfiguration"/>.</param>
        /// <param name="hostMetadata">The host configuration metadata.</param>
        /// <param name="logger">The <see cref="ILogger"/> that can be used to log trace events.</param>
        protected ScriptBindingProvider(IOptions<JobHostOptions> hostOptions, JObject hostMetadata, ILogger logger)
        {
            _hostOptions = hostOptions;
            Metadata = hostMetadata;
            Logger = logger;
        }

        /// <summary>
        /// Gets the <see cref="JobHostConfiguration"/>.
        /// </summary>
        protected JobHostOptions HostOptions => _hostOptions.Value;

        /// <summary>
        /// Gets the host configuration metadata.
        /// </summary>
        protected JObject Metadata { get; private set; }

        /// <summary>
        /// Gets the <see cref="ILogger"/> that can be used to log trace events.
        /// </summary>
        protected ILogger Logger { get; private set; }

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
        public virtual bool TryResolveAssembly(string assemblyName, AssemblyLoadContext targetContext, out Assembly assembly)
        {
            assembly = null;
            return false;
        }
    }
}
