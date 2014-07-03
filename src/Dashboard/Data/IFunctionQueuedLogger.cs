namespace Dashboard.Data
{
    public interface IFunctionQueuedLogger
    {
        void LogFunctionQueued(FunctionInstanceSnapshot snapshot);
    }
}
