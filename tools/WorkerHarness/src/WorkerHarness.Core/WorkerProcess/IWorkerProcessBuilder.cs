using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkerHarness.Core.Worker;

namespace WorkerHarness.Core.WorkerProcess
{
    public interface IWorkerProcessBuilder
    {
        Process Build(WorkerDescription workerDescription, string workerId, string requestId);
    }
}
