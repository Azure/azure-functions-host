// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Eventing.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public delegate ILanguageWorkerChannel CreateChannel(WorkerConfig conf, IObservable<FunctionRegistrationContext> registrations, int attemptCount);

    public interface ILanguageWorkerChannel : IDisposable
    {
        string Id { get; }

        IRpcServer RpcServer { get; set; }

        RpcEvent InitEvent { get; }

        WorkerConfig Config { get; }

        void WorkerReady(IObservable<FunctionRegistrationContext> functionRegistrations);

        void StartWorkerProcess(string scriptRootPath);

        void InitializeWorker();

        void SetupLanguageWorkerChannel(ScriptJobHostOptions scriptConfig);
    }
}
