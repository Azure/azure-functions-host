using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebJobs.Script
{
    public enum ExecutionStage
    {
        Started,
        InProgress,
        Finished,
        Failed,
        Succeeded
    }
}
