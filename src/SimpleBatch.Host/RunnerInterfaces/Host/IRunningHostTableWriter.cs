namespace RunnerInterfaces
{
    public interface IRunningHostTableWriter
    {
        void SignalHeartbeat(string hostName);
    }
}
