namespace Microsoft.Azure.Jobs
{
    internal interface IConnectionStringProvider
    {
        string GetConnectionString(string connectionStringName);
    }
}
