using System;
using System.Configuration;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class AmbientConnectionStringProvider : IConnectionStringProvider
    {
        /// <summary>
        /// Reads a connection string from the connectionStrings configuration section, or from an environment variable
        /// if it is missing from the configuration file, or is an empty string.
        /// </summary>
        /// <param name="connectionStringName">The name of the connection string to look up.</param>
        /// <returns>The connection string, or <see langword="null"/> if no connection string was found.</returns>
        public string GetConnectionString(string connectionStringName)
        {
            string connectionStringInConfig = null;
            var connectionStringEntry = ConfigurationManager.ConnectionStrings[connectionStringName];
            if (connectionStringEntry != null)
            {
                connectionStringInConfig = connectionStringEntry.ConnectionString;
            }

            if (!String.IsNullOrEmpty(connectionStringInConfig))
            {
                return connectionStringInConfig;
            }

            return Environment.GetEnvironmentVariable(connectionStringName) ?? connectionStringInConfig;
        }
    }
}