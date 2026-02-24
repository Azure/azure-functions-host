// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class WebHostOptionsLoggingTests
    {
        [Fact]
        public void WebHostContainer_RegistersWebJobsOptionsFactory_ForFormattableOptions()
        {
            var services = new ServiceCollection();
            var startup = new Startup(null);
            startup.ConfigureServices(services);

            services.AddSingleton<IEnvironment>(_ => new TestEnvironment());

            using var serviceProvider = services.BuildServiceProvider();

            // Resolve the IOptionsFactory for a WebHost-level options type that implements IOptionsFormatter.
            // AddFormattableOptionsLogging replaces the default OptionsFactory with WebJobsOptionsFactory,
            // which will log the options when they are created.
            var factory = serviceProvider.GetRequiredService<IOptionsFactory<ResponseCompressionOptions>>();
            Assert.Equal("WebJobsOptionsFactory`1", factory.GetType().Name);
        }

        [Fact]
        public void WebHostContainer_RegistersWebJobsOptionsFactory_ForHttpBodyControlOptions()
        {
            var services = new ServiceCollection();
            var startup = new Startup(null);
            startup.ConfigureServices(services);

            services.AddSingleton<IEnvironment>(_ => new TestEnvironment());

            using var serviceProvider = services.BuildServiceProvider();

            var factory = serviceProvider.GetRequiredService<IOptionsFactory<HttpBodyControlOptions>>();
            Assert.Equal("WebJobsOptionsFactory`1", factory.GetType().Name);
        }

        [Fact]
        public void WebHostContainer_RegistersOptionsLoggingService()
        {
            var services = new ServiceCollection();
            var startup = new Startup(null);
            startup.ConfigureServices(services);

            // Verify OptionsLoggingService is registered as an IHostedService via service descriptors.
            Assert.Contains(services, d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType is not null
                && string.Equals(d.ImplementationType.Name, "OptionsLoggingService", System.StringComparison.Ordinal));
        }

        [Fact]
        public void ChildContainer_GetsOwnOptionsLoggingSource()
        {
            // Both the WebHost and ScriptHost call AddFormattableOptionsLogging(), which registers
            // an IOptionsLoggingSource backed by a BufferBlock<string>. Each OptionsLoggingService
            // (IHostedService) reads from its container's IOptionsLoggingSource and writes to its
            // container's ILogger. If both containers shared the same IOptionsLoggingSource, messages
            // would be randomly consumed by either service (competing consumers), causing ScriptHost
            // options to be logged by the WebHost logger (missing from App Insights) or vice versa.
            //
            // This test verifies that CreateChildContainer + the ScriptHost's own registration
            // produces a separate IOptionsLoggingSource, so each pipeline is fully independent.
            var rootServices = new ServiceCollection();
            rootServices.AddFormattableOptionsLogging();

            using var rootProvider = rootServices.BuildServiceProvider();

            // Resolve IOptionsLoggingSource type via reflection (internal type).
            Type optionsLoggingSourceType = typeof(IOptionsFormatter).Assembly
                .GetTypes()
                .Single(t => string.Equals(t.Name, "IOptionsLoggingSource", StringComparison.Ordinal));

            var rootSource = rootProvider.GetService(optionsLoggingSourceType);
            Assert.NotNull(rootSource);

            // Create a child container the same way JobHostScopedServiceProviderFactory does.
            var childServices = rootProvider.CreateChildContainer(rootServices);

            // Simulate ScriptHost calling AddFormattableOptionsLogging via AddWebJobs.
            // This adds a second IOptionsLoggingSource registration that wins over the cloned root one.
            var scriptHostServices = new ServiceCollection();
            scriptHostServices.AddFormattableOptionsLogging();

            foreach (var service in scriptHostServices)
            {
                childServices.Add(service);
            }

            using var childProvider = childServices.BuildServiceProvider();

            var childSource = childProvider.GetService(optionsLoggingSourceType);
            Assert.NotNull(childSource);

            // The child container must have its own IOptionsLoggingSource, not the root's.
            Assert.NotSame(rootSource, childSource);
        }
    }
}
