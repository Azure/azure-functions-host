// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

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

        /// <summary>
        /// Convert IConfiguration object to the JToken object with a section
        /// </summary>
        /// <param name="configuration">configuration object</param>
        /// <param name="section">Section that you want to fetch.</param>
        /// <returns>JToken representation of the configuration</returns>
        public static JToken Convert(this IConfiguration configuration, string section)
        {
            return Parse(configuration.GetSection(section));
        }

        private static JToken Parse(IConfigurationSection section)
        {
            var jObject = new JObject();

            var key = section.Key;
            var children = section.GetChildren();
            if (children.Count() == 0 && section.Value != null)
            {
                return jObject[key] = section.Value;
            }
            foreach (var child in children)
            {
                jObject.Add(child.Key, Parse(child));
            }
            return jObject;
        }
    }
}
