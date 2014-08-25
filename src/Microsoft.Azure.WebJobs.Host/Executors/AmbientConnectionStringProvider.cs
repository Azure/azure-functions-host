// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Configuration;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class AmbientConnectionStringProvider : IConnectionStringProvider
    {
        internal static string Prefix = "AzureWebJobs";

        public static string GetPrefixedConnectionStringName(string connectionStringName)
        {
            return Prefix + connectionStringName;
        }

        /// <summary>
        /// Reads a connection string from the connectionStrings configuration section, or from an environment variable
        /// if it is missing from the configuration file, or is an empty string.
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

        public IReadOnlyDictionary<string, string> GetConnectionStrings()
        {
            var connectionStrings = new Dictionary<string, string>();

            foreach (ConnectionStringSettings connectionString in ConfigurationManager.ConnectionStrings)
            {
                if (String.IsNullOrEmpty(connectionString.ConnectionString))
                {
                    connectionStrings.Add(connectionString.Name, Environment.GetEnvironmentVariable(connectionString.Name));
                }
                else
                {
                    connectionStrings.Add(connectionString.Name, connectionString.ConnectionString);
                }
            }

            return connectionStrings;
        }
    }
}
