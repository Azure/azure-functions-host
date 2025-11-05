// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

#nullable enable

namespace Microsoft.Extensions.Logging
{
    public static class ILoggingBuilderExtensions
    {
        public static ILoggingBuilder AddWebJobsSystem<T>(this ILoggingBuilder builder)
            where T : SystemLoggerProvider
        {
            builder.Services.AddSingleton<ILoggerProvider, T>();

            // Log all logs to SystemLogger
            builder.AddDefaultWebJobsFilters<T>(LogLevel.Trace);
            return builder;
        }
    }
}
