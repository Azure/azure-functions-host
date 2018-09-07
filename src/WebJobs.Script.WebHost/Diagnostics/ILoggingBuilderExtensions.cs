// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.Logging
{
    public static class ILoggingBuilderExtensions
    {
        public static void AddWebJobsSystem<T>(this ILoggingBuilder builder) where T : SystemLoggerProvider
        {
            // Do not filter the SystemLogger
            builder.Services.AddSingleton<ILoggerProvider, T>();

            // Only log debug-level logs to SystemLogger
            builder.AddFilter<T>(null, LogLevel.Debug);
        }

        public static void AddDeferred(this ILoggingBuilder builder)
        {
            // Do not filter this. It will be filtered internally.
            builder.Services.AddSingleton<DeferredLoggerProvider>();

            // The ASP.NET host startup will ask for loggers, but not all services are ready,
            // so this will be null. The runtime starutp will correctly populate the services.
            builder.Services.AddSingleton<ILoggerProvider>(s => (ILoggerProvider)s.GetService<DeferredLoggerProvider>() ?? NullLoggerProvider.Instance);
            builder.Services.AddSingleton<IDeferredLogSource>(s => s.GetRequiredService<DeferredLoggerProvider>());

            builder.AddFilter<DeferredLoggerProvider>(_ => true);
        }
    }
}
