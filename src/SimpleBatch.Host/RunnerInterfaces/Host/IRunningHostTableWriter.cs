namespace Microsoft.WindowsAzure.Jobs
{
    public interface IRunningHostTableWriter
    {
        void SignalHeartbeat(string hostName);
    }
}
