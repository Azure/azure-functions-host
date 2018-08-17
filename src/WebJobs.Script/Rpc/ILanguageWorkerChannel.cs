// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal delegate ILanguageWorkerChannel CreateChannel(WorkerConfig conf, IObservable<FunctionRegistrationContext> registrations, int attemptCount);

    internal interface ILanguageWorkerChannel : IDisposable
    {
        string Id { get; }

        WorkerConfig Config { get; }
    }
}
