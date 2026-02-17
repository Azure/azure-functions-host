// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.Logging
{
    public static class ScriptLoggingBuilderExtensions
    {
        private static readonly ConcurrentDictionary<string, bool> _filteredCategoryCache = new();

        // High-volume extension categories whose Debug/Trace logs are
        // suppressed to reduce noise in the FunctionsLogs table.
        private static readonly HashSet<string> _suppressedCategories = new(StringComparer.Ordinal)
        {
            "Microsoft.Azure.WebJobs.Extensions.Storage.Common.Listeners.QueueListener",
            "Microsoft.Azure.WebJobs.EventHubs.EventHubProducerClientImpl",
            "Host.Executor"
        };

        public static ILoggingBuilder AddForwardingLogger(this ILoggingBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Services.TryAddKeyedSingleton<ILoggerFactory, ForwardingLoggerFactory>(
                ForwardingLogger.ServiceKey);
            builder.Services.TryAddKeyedSingleton(
                typeof(ILogger<>), ForwardingLogger.ServiceKey, typeof(ForwardingLogger<>));
            return builder;
        }

        public static ILoggingBuilder AddDefaultWebJobsFilters(this ILoggingBuilder builder)
        {
            builder.SetMinimumLevel(LogLevel.None);
            builder.AddFilter((c, l) => Filter(c, l, LogLevel.Information));
            return builder;
        }

        public static ILoggingBuilder AddDefaultWebJobsFilters<T>(this ILoggingBuilder builder, LogLevel level)
            where T : ILoggerProvider
        {
            builder.AddFilter<T>(null, LogLevel.None);
            builder.AddFilter<T>((c, l) => Filter(c, l, level));
            return builder;
        }

        internal static bool Filter(string category, LogLevel actualLevel, LogLevel minLevel)
        {
            if (actualLevel < minLevel || !IsFiltered(category))
            {
                return false;
            }

            // Suppress Debug/ Trace from high-volume extension categories.
            if (actualLevel < LogLevel.Information && IsSuppressedCategory(category))
            {
                return false;
            }

            return true;
        }

        private static bool IsSuppressedCategory(string category)
        {
            return _suppressedCategories.Contains(category);
        }

        private static bool IsFiltered(string category)
        {
            return _filteredCategoryCache.GetOrAdd(
                category,
                static cat => ScriptConstants.SystemLogCategoryPrefixes.Any(p => cat.StartsWith(p)));
        }

        public static void AddConsoleIfEnabled(this ILoggingBuilder builder, HostBuilderContext context)
        {
            builder.AddConsoleIfEnabled(context.HostingEnvironment.IsDevelopment(), context.Configuration);
        }

        private static void AddConsoleIfEnabled(
            this ILoggingBuilder builder, bool isDevelopment, IConfiguration configuration)
        {
            // console logging defaults to false, except for self host
            bool enableConsole = isDevelopment;

            string consolePath = ConfigurationPath.Combine(
                ConfigurationSectionNames.JobHost, "Logging", "Console", "IsEnabled");
            IConfigurationSection configSection = configuration.GetSection(consolePath);

            if (configSection.Exists())
            {
                // if it has been explicitly configured that value overrides default
                enableConsole = configSection.Get<bool>();
            }

            if (enableConsole)
            {
                builder.AddConsole()
                       // Tooling console json log entries are meant to be used by IDEs / Debuggers.
                       // Users are not supposed to set the log level for this category via host.JSON logging settings.
                       .AddFilter(WorkerConstants.ToolingConsoleLogCategoryName, LogLevel.Information);
            }
        }
    }
}
