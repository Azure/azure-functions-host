namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal interface IConnectionStringProvider
    {
        string GetConnectionString(string connectionStringName);
    }
}
