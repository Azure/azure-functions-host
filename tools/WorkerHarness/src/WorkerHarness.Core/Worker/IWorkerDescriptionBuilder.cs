using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core.Worker
{
    public interface IWorkerDescriptionBuilder
    {
        WorkerDescription Build(string workerConfigPath, string workerDirectory);
    }
}
