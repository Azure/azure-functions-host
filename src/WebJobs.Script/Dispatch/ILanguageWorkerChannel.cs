// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Description.Script;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    // wrapper around proc.start & grpc channel with some state management
    internal interface ILanguageWorkerChannel : IDisposable
    {
        Task StartAsync();

        Task StopAsync();

        Task HandleFileEventAsync(FileSystemEventArgs fileEvent);

        void LoadAsync(FunctionMetadata functionMetadata);

        Task<ScriptInvocationResult> InvokeAsync(FunctionMetadata functionMetadata, ScriptInvocationContext invokeContext);
    }
}
