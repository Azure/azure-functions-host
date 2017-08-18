// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Scaling;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Scale
{
    /// <summary>
    /// Responsible for examining current host load status and resolving that
    /// into a single load factor number.
    /// </summary>
    public class WorkerStatusProvider : IWorkerStatusProvider
    {
        private readonly HostPerformanceManager _hostPerformanceManager = null;
        private readonly TraceWriter _traceWriter = null;

        public WorkerStatusProvider(HostPerformanceManager hostPerformanceManager, TraceWriter traceWriter)
        {
            _hostPerformanceManager = hostPerformanceManager;
            _traceWriter = traceWriter;
        }

        public Task<int> GetWorkerStatus(string activityId)
        {
            int loadFactor = DetermineLoadFactor();
            return Task.FromResult(loadFactor);
        }

        internal int DetermineLoadFactor()
        {
            Collection<string> exceededCounters = new Collection<string>();
            if (_hostPerformanceManager.IsUnderHighLoad(exceededCounters))
            {
                _traceWriter.Warning($"Thresholds for the following counters have been exceeded: [{string.Join(", ", exceededCounters)}]");

                return ScaleSettings.DefaultBusyWorkerLoadFactor;
            }
            else
            {
                return ScaleSettings.DefaultFreeWorkerLoadFactor;
            }
        }
    }
}