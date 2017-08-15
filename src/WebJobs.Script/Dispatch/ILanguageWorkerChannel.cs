// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Description.Script;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal interface ILanguageWorkerChannel : IDisposable
    {
        void Register(FunctionMetadata functionMetadata);

        Task<ScriptInvocationResult> InvokeAsync(FunctionMetadata functionMetadata, ScriptInvocationContext invokeContext);
    }
}
