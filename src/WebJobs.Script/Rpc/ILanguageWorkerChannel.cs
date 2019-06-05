// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public interface ILanguageWorkerChannel : IDisposable
    {
        string Id { get; }

        IDictionary<string, BufferBlock<ScriptInvocationContext>> FunctionInputBuffers { get; }

        LanguageWorkerChannelState State { get; }

        void SetupFunctionInvocationBuffers(IEnumerable<FunctionMetadata> functions);

        void SendFunctionLoadRequests();

        void SendFunctionEnvironmentReloadRequest();

        Task StartWorkerProcessAsync();
    }
}
