// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class FunctionRegistrationContext
    {
        public FunctionMetadata Metadata { get; set; }

        // A buffer block containing function invocations
        public BufferBlock<ScriptInvocationContext> InputBuffer { get; set; }
    }
}
