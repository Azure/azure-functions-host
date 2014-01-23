
namespace Microsoft.WindowsAzure.Jobs
{
    internal class NullRunningHostTableWriter : IRunningHostTableWriter
    {
        public void SignalHeartbeat(string hostName)
        {
        }
    }
}
