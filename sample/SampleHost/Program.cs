// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host.Loggers;
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

            config.Tracing.ConsoleLevel = TraceLevel.Off;

            // Build up a LoggerFactory to log to App Insights, but only if this key exists.
            string instrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
            if (!string.IsNullOrEmpty(instrumentationKey))
            {
                // build up log filters
                LogCategoryFilter logCategoryFilter = new LogCategoryFilter();
                logCategoryFilter.DefaultLevel = LogLevel.Debug;
                logCategoryFilter.CategoryLevels[LogCategories.Function] = LogLevel.Debug;
                logCategoryFilter.CategoryLevels[LogCategories.Results] = LogLevel.Debug;
                logCategoryFilter.CategoryLevels[LogCategories.Aggregator] = LogLevel.Debug;

                config.LoggerFactory = new LoggerFactory()
                    .AddApplicationInsights(instrumentationKey, logCategoryFilter.Filter)
                    .AddConsole(logCategoryFilter.Filter);
            }

            config.CreateMetadataProvider().DebugDumpGraph(Console.Out);

            var host = new JobHost(config);
            host.RunAndBlock();
        }
    }
}
