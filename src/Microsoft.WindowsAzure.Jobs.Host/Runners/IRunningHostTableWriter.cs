namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IRunningHostTableWriter
    {
        void SignalHeartbeat(string hostName);
    }
}
