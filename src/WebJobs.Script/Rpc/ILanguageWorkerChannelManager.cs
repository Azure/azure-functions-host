// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public interface ILanguageWorkerChannelManager
    {
        Task<ILanguageWorkerChannel> InitializeChannelAsync(string language);

        IEnumerable<ILanguageWorkerChannel> GetChannels(string language);

        Task SpecializeAsync();

        bool ShutdownChannelIfExists(string language, string workerId);

        void ShutdownChannels();

        ILanguageWorkerChannel CreateLanguageWorkerChannel(string workerId, string scriptRootPath, string language, IMetricsLogger metricsLogger, int attemptCount, bool isWebhostChannel = false, IOptions<ManagedDependencyOptions> managedDependencyOptions = null);
    }
}
