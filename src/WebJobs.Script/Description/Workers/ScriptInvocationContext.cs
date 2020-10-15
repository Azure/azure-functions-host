// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ScriptInvocationContext
    {
        public FunctionMetadata FunctionMetadata { get; set; }

        public ExecutionContext ExecutionContext { get; set; }

        public string Traceparent { get; set; }

        public string Tracestate { get; set; }

        public IEnumerable<KeyValuePair<string, string>> Attributes { get; set; }

        public IEnumerable<(string name, DataType type, object val)> Inputs { get; set; }

        public Dictionary<string, object> BindingData { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public TaskCompletionSource<ScriptInvocationResult> ResultSource { get; set; }

        public ILogger Logger { get; set; }

        public System.Threading.ExecutionContext AsyncExecutionContext { get; set; }

        /// <summary>
        /// Gets or sets the list of names of <see cref="MemoryMappedFile"/> that were allocated to
        /// transfer data to the worker process for this invcation.
        /// These are tracked here so that once the invocation is complete, the resources can be
        /// freed.
        /// </summary>
        public IList<string> SharedMemoryResources { get; set; }
    }
}
