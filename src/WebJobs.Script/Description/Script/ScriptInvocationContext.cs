// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description.Script
{
    public class ScriptInvocationContext
    {
        public FunctionMetadata FunctionMetadata { get; set; }

        public ExecutionContext ExecutionContext { get; set; }

        public IEnumerable<(string name, DataType type, object val)> Inputs { get; set; }

        public Dictionary<string, object> BindingData { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public TaskCompletionSource<ScriptInvocationResult> ResultSource { get; set; }

        public TraceWriter TraceWriter { get; set; }

        public ILogger Logger { get; set; }
    }
}
