// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Logging
{
    public static class ILoggingBuilderExtensions
    {
        public static void AddWebJobsSystem<T>(this ILoggingBuilder builder) where T : SystemLoggerProvider
        {
            builder.Services.AddSingleton<ILoggerProvider, T>();

            // Log all logs to SystemLogger
            builder.AddDefaultWebJobsFilters<T>(LogLevel.Trace);
        }

        public static void AddDeferred(this ILoggingBuilder builder)
        {
            // Do not filter this. It will be filtered internally.
            builder.Services.AddSingleton<DeferredLoggerProvider>();
            builder.AddFilter<DeferredLoggerProvider>(_ => true);

            builder.Services.AddSingleton<ILoggerProvider>(s => s.GetRequiredService<DeferredLoggerProvider>());
            builder.Services.AddSingleton<IDeferredLogSource>(s => s.GetRequiredService<DeferredLoggerProvider>());
        }
    }
}
