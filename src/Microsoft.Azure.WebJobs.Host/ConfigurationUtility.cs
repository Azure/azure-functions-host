// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Host
{
    internal static class ConfigurationUtility
    {
        private static Lazy<IConfigurationRoot> _configuration = new Lazy<IConfigurationRoot>(BuildConfiguration);

        private static IConfigurationRoot Configuration => _configuration.Value;

        public static string GetSettingFromConfigOrEnvironment(string settingName)
        {
            if (string.IsNullOrEmpty(settingName))
            {
                return null;
            }

            return Configuration[settingName];
        }

        // The fallback to reading the connection string from the configuration/app setting
        // is here to maintain legacy behavior. Should we keep this?
        public static string GetConnectionFromConfigOrEnvironment(string connectionName)
            => Configuration.GetConnectionString(connectionName) ?? Configuration[connectionName];

        private static IConfigurationRoot BuildConfiguration()
        {
            var configurationBuilder = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddJsonFile("appsettings.json", optional: true);

            return configurationBuilder.Build();
        }

        internal static void Reset()
        {
            _configuration = new Lazy<IConfigurationRoot>(BuildConfiguration);
        }
    }
}
