// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public interface ILanguageWorkerChannelManager
    {
        Task InitializeAsync();

        ILanguageWorkerChannel GetChannel(string language);

        Task SpecializeAsync();

        bool ShutdownChannelIfExists(string language);

        Task ShutdownChannelsAsync();

        ILanguageWorkerChannel CreateLanguageWorkerChannel(string workerId, string scriptRootPath, string language, IObservable<FunctionRegistrationContext> functionRegistrations, IMetricsLogger metricsLogger, int attemptCount);
    }
}
