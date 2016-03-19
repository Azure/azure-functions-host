// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;

namespace Dashboard.Data
{
    public static class ConnectionStringProvider
    {
        internal const string Prefix = "AzureWebJobs";

        public static string GetPrefixedConnectionStringName(string connectionStringName)
        {
            return Prefix + connectionStringName;
        }

        public static string GetConnectionString(string connectionStringName)
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

        /// <summary>
        /// Return a dictionary of all app settings and connection strings. Since for multi storage
        /// account support the SDK supports arbitrarily named connection string settings, we must
        /// optimistically include ALL App Settings / Connection Strings in the result.
        /// </summary>
        public static IReadOnlyDictionary<string, string> GetPossibleConnectionStrings()
        {
            Dictionary<string, string> connectionStrings = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            IDictionary environmentVariables = Environment.GetEnvironmentVariables();

            foreach (string key in environmentVariables.Keys)
            {
                connectionStrings[key] = (string)environmentVariables[key];
            }

            // Connection string settings take precedence over environment variables in
            // case of name conflicts
            foreach (ConnectionStringSettings setting in ConfigurationManager.ConnectionStrings)
            {
                connectionStrings[setting.Name] = setting.ConnectionString;
            }

            return connectionStrings;
        }
    }
}
