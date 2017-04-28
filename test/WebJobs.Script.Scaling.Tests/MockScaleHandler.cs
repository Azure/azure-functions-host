// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class MockScaleHandler : IScaleHandler
    {
        public MockScaleHandler()
        {
        }

        public virtual Task<string> AddWorker(string activityId, IEnumerable<string> stampNames, int workers)
        {
            return Task.FromResult(stampNames.FirstOrDefault());
        }

        public virtual Task RemoveWorker(string activityId, IWorkerInfo workerInfo)
        {
            return Task.CompletedTask;
        }

        public virtual Task<bool> PingWorker(string activityId, IWorkerInfo workerInfo)
        {
            return Task.FromResult(true);
        }
    }
}