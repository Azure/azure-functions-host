// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Connection string provider that reads from configuration first, and if a connection
    /// is not found there, will search in environment variables.
    /// </summary>
    public class AmbientConnectionStringProvider : IConnectionStringProvider
    {
        private static readonly AmbientConnectionStringProvider Singleton = new AmbientConnectionStringProvider();

        internal static readonly string Prefix = "AzureWebJobs";

        private AmbientConnectionStringProvider()
        {
        }

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static AmbientConnectionStringProvider Instance
        {
            get { return Singleton; }
        }

        /// <summary>
        /// Attempts to first read a connection string from the connectionStrings configuration section.
        /// If not found there, it will attempt to read from environment variables.
        /// </summary>
        /// <param name="connectionStringName">The name of the connection string to look up.</param>
        /// <returns>The connection string, or <see langword="null"/> if no connection string was found.</returns>
        public string GetConnectionString(string connectionStringName)
        {
            string prefixedConnectionStringName = GetPrefixedConnectionStringName(connectionStringName);
            string connectionStringInConfig = null;
            var connectionStringEntry = ConfigurationManager.ConnectionStrings[prefixedConnectionStringName];
            if (connectionStringEntry != null)
            {
                connectionStringInConfig = connectionStringEntry.ConnectionString;
            }

            if (!String.IsNullOrEmpty(connectionStringInConfig))
            {
                return connectionStringInConfig;
            }

            return Environment.GetEnvironmentVariable(prefixedConnectionStringName) ?? connectionStringInConfig;
        }

        internal static string GetPrefixedConnectionStringName(string connectionStringName)
        {
            return Prefix + connectionStringName;
        }
    }
}
