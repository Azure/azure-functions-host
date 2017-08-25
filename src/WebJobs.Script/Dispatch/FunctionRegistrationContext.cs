// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Description.Script;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    public class FunctionRegistrationContext
    {
        public FunctionMetadata Metadata { get; set; }

        public BufferBlock<ScriptInvocationContext> InputBuffer { get; set; }
    }
}
