// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Scaling;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    /// <summary>
    /// Responsible for examining current host load status and resolving that
    /// into a single load factor number.
    /// </summary>
    public class WorkerStatusProvider : IWorkerStatusProvider
    {
        private readonly HostPerformanceManager _hostPerformanceManager;
        private readonly ILoadFactorProvider _functionLoadFactorProvider;
        private readonly TraceWriter _traceWriter = null;

        public WorkerStatusProvider(HostPerformanceManager hostPerformanceManager, ILoadFactorProvider functionLoadProvider, TraceWriter traceWriter)
        {
            _hostPerformanceManager = hostPerformanceManager;
            _functionLoadFactorProvider = functionLoadProvider;
            _traceWriter = traceWriter;
        }

        public Task<int> GetWorkerStatus(string activityId)
        {
            int loadFactor = DetermineLoadFactor();
            return Task.FromResult(loadFactor);
        }

        internal int DetermineLoadFactor()
        {
            // check host health
            Collection<string> exceededCounters = new Collection<string>();
            if (_hostPerformanceManager.IsUnderHighLoad(exceededCounters))
            {
                _traceWriter.Warning($"Thresholds for the following counters have been exceeded: [{string.Join(", ", exceededCounters)}]");

                return ScaleSettings.DefaultBusyWorkerLoadFactor;
            }

            // check functions
            // will return a load factor between 0 and 1
            // scale to the range used
            var loadFactor = _functionLoadFactorProvider.GetLoadFactor() * 100;

            // 0 - 20 : worker is free and can be scaled down
            // 20 - 50 : worker is not very busy
            // 50 : worker is stable and keeping up with work
            // 50 - 79 : working is getting more busy
            // 80 - 100 : worker is busy/overloaded and we should scale out
            return (int)loadFactor;
        }
    }
}