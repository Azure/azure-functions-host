namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IConnectionStringProvider
    {
        string GetConnectionString(string connectionStringName);
    }
}