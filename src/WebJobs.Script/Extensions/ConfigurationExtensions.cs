// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    internal static class ConfigurationExtensions
    {
        internal static IConfigurationSection GetWebJobsConnectionStringSection(this IConfiguration configuration, string connectionStringName)
        {
            // first try prefixing
            string prefixedConnectionStringName = IConfigurationExtensions.GetPrefixedConnectionStringName(connectionStringName);
            IConfigurationSection section = GetConnectionStringOrSettingSection(configuration, prefixedConnectionStringName);

            if (!section.Exists())
            {
                // next try a direct unprefixed lookup
                section = GetConnectionStringOrSettingSection(configuration, connectionStringName);
            }

            return section;
        }

        /// <summary>
        /// Looks for a connection string by first checking the ConfigurationStrings section, and then the root.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="connectionName">The connection string key.</param>
        /// <returns>Configuration section corresponding to the connectionName</returns>
        private static IConfigurationSection GetConnectionStringOrSettingSection(this IConfiguration configuration, string connectionName)
        {
            var connectionStringSection = configuration?.GetSection("ConnectionStrings").GetSection(connectionName);

            if (connectionStringSection.Exists())
            {
                return connectionStringSection;
            }
            return configuration?.GetSection(connectionName);
        }
    }
}
