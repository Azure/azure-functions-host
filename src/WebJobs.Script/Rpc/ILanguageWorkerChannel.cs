// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public delegate ILanguageWorkerChannel CreateChannel(string language, IObservable<FunctionRegistrationContext> registrations, int attemptCount);

    public interface ILanguageWorkerChannel : IDisposable
    {
        string Id { get; }

        WorkerConfig Config { get; }

        void RegisterFunctions(IObservable<FunctionRegistrationContext> functionRegistrations);

        void SendFunctionEnvironmentReloadRequest();

        void StartWorkerProcess();
    }
}
