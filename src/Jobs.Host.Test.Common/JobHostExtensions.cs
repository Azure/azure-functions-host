using Microsoft.WindowsAzure.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.WindowsAzure.Jobs.Test
{
    public static class JobHostExtensions
    {
        // Run one iteration through the host. This does as much work as possible, and then returns. 
        // It won't loop and poll.
        public static void RunOneIteration(this JobHost host)
        {
            var cts = new CancellationTokenSource();
            host.RunAndBlock(cts.Token, () => { cts.Cancel(); });
        }
    }
}
