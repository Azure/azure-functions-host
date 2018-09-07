// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.Logging
{
    public static class ILoggingBuilderExtensions
    {
        public static void AddConsoleIfEnabled(this ILoggingBuilder builder, HostBuilderContext context)
        {
            AddConsoleIfEnabled(builder, context.HostingEnvironment.IsDevelopment(), context.Configuration);
        }

        public static void AddConsoleIfEnabled(this ILoggingBuilder builder, WebHostBuilderContext context)
        {
            AddConsoleIfEnabled(builder, context.HostingEnvironment.IsDevelopment(), context.Configuration);
        }

        private static void AddConsoleIfEnabled(ILoggingBuilder builder, bool isDevelopment, IConfiguration configuration)
        {
            // console logging defaults to false, except for self host
            bool enableConsole = isDevelopment;

            if (!enableConsole)
            {
                string consolePath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "Logging", "Console", "IsEnabled");
                IConfigurationSection configSection = configuration.GetSection(consolePath);

                if (configSection.Exists())
                {
                    // if it has been explicitly configured that value overrides default
                    enableConsole = configSection.Get<bool>();
                }
            }

            if (enableConsole)
            {
                builder.AddConsole();
            }
        }
    }
}
