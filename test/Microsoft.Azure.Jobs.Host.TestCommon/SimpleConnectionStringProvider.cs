namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    public class SimpleConnectionStringProvider : IConnectionStringProvider
    {
        public string StorageConnectionString { get; set; }

        public string DashboardConnectionString { get; set; }

        public string GetConnectionString(string connectionStringName)
        {
            if (connectionStringName == JobHost.DashboardConnectionStringName)
            {
                return DashboardConnectionString;
            }
            else if (connectionStringName == JobHost.StorageConnectionStringName)
            {
                return StorageConnectionString;
            }
            else
            {
                return null;
            }
        }
    }
}
