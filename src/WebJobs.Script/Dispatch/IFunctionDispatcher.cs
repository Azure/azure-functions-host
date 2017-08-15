// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Description.Script;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    // maps from language type -> language worker pool
    internal interface IFunctionDispatcher : IDisposable
    {
        // assign functions to worker pools based on language type
        // return false if FunctionMetadata is unsupported
        // start worker if necessary
        bool TryRegister(FunctionMetadata functionMetadata);

        // invoke a function
        // could use delay loading for start worker / load fucntion
        Task<ScriptInvocationResult> InvokeAsync(FunctionMetadata functionMetadata, ScriptInvocationContext context);
    }
}
