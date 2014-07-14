using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    public class NullConnectionStringProvider : IConnectionStringProvider
    {
        public string GetConnectionString(string connectionStringName)
        {
            return null;
        }
    }
}
