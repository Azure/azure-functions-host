// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public interface IFunctionDispatcher : IDisposable
    {
        FunctionDispatcherState State { get; }

        // Tests if the function metadata is supported by a known language worker
        bool IsSupported(FunctionMetadata metadata, string language);

        Task InvokeAsync(ScriptInvocationContext invocationContext);

        Task InitializeAsync(IEnumerable<FunctionMetadata> functions);
    }
}
