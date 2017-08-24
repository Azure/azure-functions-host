using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public interface IWorkerProcessFactory
    {
        Process CreateWorkerProcess(WorkerCreateContext context);
    }
}
