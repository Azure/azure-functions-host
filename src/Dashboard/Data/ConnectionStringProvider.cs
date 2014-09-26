// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;

namespace Dashboard.Data
{
    public static class ConnectionStringProvider
    {
        internal static string Prefix = "AzureWebJobs";

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

        public static IReadOnlyDictionary<string, string> GetConnectionStrings()
        {
            Dictionary<string, string> connectionStrings = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            IDictionary environmentVariables = Environment.GetEnvironmentVariables();

            foreach (string key in environmentVariables.Keys)
            {
                if (key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    connectionStrings[key.Substring(Prefix.Length)] = (string)environmentVariables[key];
                }
            }

            // Connection string settings take precedence over environment variables.
            foreach (ConnectionStringSettings setting in ConfigurationManager.ConnectionStrings)
            {
                if (setting.Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    connectionStrings[setting.Name.Substring(Prefix.Length)] = setting.ConnectionString;
                }
            }

            return connectionStrings;
        }
    }
}
