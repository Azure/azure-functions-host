// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Host.SingletonTests
{
    // Borrowed from WebJobs TestCommon
    static class TestUtilities
    {
        public static StorageAccount GetStorageAccount(this IHost host)
        {
            var provider = host.Services.GetRequiredService<StorageAccountProvider>(); // $$$ ok?
            return provider.GetHost();
        }

        public static IHostBuilder ConfigureDefaultTestHost(this IHostBuilder builder, Action<IWebJobsBuilder> configureWebJobs, params Type[] types)
        {
            return builder.ConfigureWebJobs(configureWebJobs)
                .ConfigureAppConfiguration(c =>
                {
                    c.AddTestSettings();
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ITypeLocator>(new TestTypeLocator(types));
                })
                .ConfigureTestLogger();
        }

        public static IHostBuilder ConfigureDefaultTestHost<TProgram>(this IHostBuilder builder, Action<IWebJobsBuilder> configureWebJobs)
        {
            return builder.ConfigureDefaultTestHost(configureWebJobs, typeof(TProgram))
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IJobHost, JobHost<TProgram>>();
                });
        }

        public static IHostBuilder ConfigureTestLogger(this IHostBuilder builder)
        {
            return builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(new TestLoggerProvider());
            });
        }

        public static JobHost GetJobHost(this IHost host)
        {
            return host.Services.GetService<IJobHost>() as JobHost;
        }
    }

    internal class JobHost<TProgram> : JobHost
    {
        private readonly IJobActivator _jobActivator;

        public JobHost(
            IOptions<JobHostOptions> options,
            IJobHostContextFactory contextFactory,
            IJobActivator jobActivator)
            : base(options, contextFactory)
        {
            _jobActivator = jobActivator;
        }
    }
}
