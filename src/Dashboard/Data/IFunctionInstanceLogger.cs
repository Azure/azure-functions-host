namespace Dashboard.Data
{
    internal interface IFunctionInstanceLogger
    {
        void LogFunctionStarted(FunctionInstanceSnapshot snapshot);

        void LogFunctionCompleted(FunctionInstanceSnapshot snapshot);
    }
}
