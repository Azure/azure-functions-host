// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;

namespace SampleHost
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new JobHostConfiguration();
            config.Queues.VisibilityTimeout = TimeSpan.FromSeconds(15);
            config.Queues.MaxDequeueCount = 3;

            if (config.IsDevelopment)
            {
                config.UseDevelopmentSettings();
            }

            CheckAndEnableAppInsights(config);

            var host = new JobHost(config);
            host.RunAndBlock();
        }

        private static void CheckAndEnableAppInsights(JobHostConfiguration config)
        {
            // If AppInsights is enabled, build up a LoggerFactory
            string instrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
            if (!string.IsNullOrEmpty(instrumentationKey))
            {
                var filter = new LogCategoryFilter();
                filter.DefaultLevel = LogLevel.Debug;
                filter.CategoryLevels[LogCategories.Function] = LogLevel.Debug;
                filter.CategoryLevels[LogCategories.Results] = LogLevel.Debug;
                filter.CategoryLevels[LogCategories.Aggregator] = LogLevel.Debug;

                config.LoggerFactory = new LoggerFactory()
                    .AddApplicationInsights(instrumentationKey, filter.Filter)
                    .AddConsole(filter.Filter);
            }
        }
    }
}
