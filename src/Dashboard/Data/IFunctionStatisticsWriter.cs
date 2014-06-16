namespace Dashboard.Data
{
    public interface IFunctionStatisticsWriter
    {
        void IncrementSuccess(string functionId);

        void IncrementFailure(string functionId);
    }
}
