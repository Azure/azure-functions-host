namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    public class SimpleConnectionStringProvider : IConnectionStringProvider
    {
        public string DataConnectionString { get; set; }

        public string RuntimeConnectionString { get; set; }

        public string GetConnectionString(string connectionStringName)
        {
            if (connectionStringName == JobHost.DataConnectionStringName)
            {
                return DataConnectionString;
            }
            else if (connectionStringName == JobHost.LoggingConnectionStringName)
            {
                return RuntimeConnectionString;
            }
            else
            {
                return null;
            }
        }
    }
}
