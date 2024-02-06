// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.Logging
{
    public static class ScriptLoggingBuilderExtensions
    {
        private static readonly ImmutableArray<string> SystemLogCategoryPrefixes = ImmutableArray.Create("Microsoft.Azure.Functions", "Microsoft.Azure.WebJobs.", "Function.", "Worker.", "Host.");
        private static readonly ImmutableArray<string> SystemOnlyCategorySuffixes = ImmutableArray.Create(".LinuxConsumptionMetricsTracker", ".LinuxContainerLegionMetricsPublisher");

        private static ConcurrentDictionary<string, bool> _shouldLogCategoryCache = new ConcurrentDictionary<string, bool>();
        private static ConcurrentDictionary<string, bool> _systemOnlyCategoryCache = new ConcurrentDictionary<string, bool>();

        public static ILoggingBuilder AddDefaultWebJobsFilters(this ILoggingBuilder builder)
        {
            builder.SetMinimumLevel(LogLevel.None);
            builder.AddFilter((p, c, l) => ShouldLog(p, c, l, LogLevel.Information));
            return builder;
        }

        public static ILoggingBuilder AddDefaultWebJobsFilters<T>(this ILoggingBuilder builder, LogLevel level) where T : ILoggerProvider
        {
            builder.AddFilter<T>(null, LogLevel.None);
            builder.AddFilter<T>((c, l) => ShouldLog(typeof(T).FullName, c, l, level));
            return builder;
        }

        internal static bool ShouldLog(string provider, string category, LogLevel actualLevel, LogLevel minLevel)
        {
            if (IsSystemOnlyLogCategory(category) && !IsSystemLoggerProvider(provider))
            {
                // Some categories are only logged to the system provider to avoid flooding user logs
                // with platform level details
                return false;
            }

            return actualLevel >= minLevel && ShouldLogCategory(category);
        }

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

            string consolePath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "Logging", "Console", "IsEnabled");
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

        private static bool IsSystemLoggerProvider(string provider)
        {
            return
                provider.EndsWith(".WebHostSystemLoggerProvider", StringComparison.OrdinalIgnoreCase) ||
                provider.EndsWith(".SystemLoggerProvider", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSystemOnlyLogCategory(string category)
        {
            return _systemOnlyCategoryCache.GetOrAdd(category, static cat => SystemOnlyCategorySuffixes.Any(s => cat.EndsWith(s, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool ShouldLogCategory(string category)
        {
            return _shouldLogCategoryCache.GetOrAdd(category, static cat => SystemLogCategoryPrefixes.Any(p => cat.StartsWith(p)));
        }
    }
}
