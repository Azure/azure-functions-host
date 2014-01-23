using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    public class DictionaryConnectionStringProvider : IConnectionStringProvider
    {
        private readonly IDictionary<string, string> _config;

        public DictionaryConnectionStringProvider(IDictionary<string, string> config)
        {
            _config = config;
        }

        public string GetConnectionString(string connectionStringName)
        {
            string connectionString;
            _config.TryGetValue(connectionStringName, out connectionString);
            return connectionString;
        }
    }
}
