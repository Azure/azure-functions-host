using System.Threading;

namespace Microsoft.WindowsAzure.Jobs.Host.TestCommon
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
