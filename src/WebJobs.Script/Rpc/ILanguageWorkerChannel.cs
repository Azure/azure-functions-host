// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public delegate Tuple<ILanguageWorkerChannel, ILanguageWorkerProcess> CreateChannel(string language, IObservable<FunctionRegistrationContext> registrations, int attemptCount);

    public interface ILanguageWorkerChannel : IDisposable
    {
        string WorkerId { get; }

        string Runtime { get; }

        void RegisterFunctions(IObservable<FunctionRegistrationContext> functionRegistrations);

        void SendFunctionEnvironmentReloadRequest();
    }
}
