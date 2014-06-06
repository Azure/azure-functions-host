namespace Dashboard.Data
{
    public interface IAbortRequestLogger
    {
        void LogAbortRequest(string queueName);

        bool HasRequestedAbort(string queueName);
    }
}
