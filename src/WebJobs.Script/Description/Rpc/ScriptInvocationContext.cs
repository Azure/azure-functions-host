// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ScriptInvocationContext
    {
        public FunctionMetadata FunctionMetadata { get; set; }

        public ExecutionContext ExecutionContext { get; set; }

        public IEnumerable<(string name, DataType type, object val)> Inputs { get; set; }

        public Dictionary<string, object> BindingData { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public TaskCompletionSource<ScriptInvocationResult> ResultSource { get; set; }

        public ILogger Logger { get; set; }

        public System.Threading.ExecutionContext AsyncExecutionContext { get; set; }
    }
}
