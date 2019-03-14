// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public interface ILanguageWorkerChannelManager
    {
        Task InitializeChannelAsync(string language);

        ILanguageWorkerChannel GetChannel(string language);

        Task SpecializeAsync();

        ILanguageWorkerProcess StartWorkerProcess(string workerId, string runtime, string rootScriptPath);

        bool ShutdownChannelIfExists(string language);

        void ShutdownStandbyChannels(IEnumerable<FunctionMetadata> functions);

        void ShutdownChannels();

        ILanguageWorkerChannel CreateLanguageWorkerChannel(string workerId, string scriptRootPath, string language, IObservable<FunctionRegistrationContext> functionRegistrations, IMetricsLogger metricsLogger, int attemptCount);
    }
}
