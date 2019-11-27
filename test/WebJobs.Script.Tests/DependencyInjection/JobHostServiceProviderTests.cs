// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.DependencyInjection
{
    public class JobHostServiceProviderTests
    {
        [Fact]
        public void Dispose_OnJobHostScope_DoesNotDisposeRootSingletonService()
        {
            var rootServices = new ServiceCollection();
            rootServices.AddSingleton<TestService>();
            var rootContainer = rootServices.BuildServiceProvider();

            // Get the root scope factory
            IServiceScopeFactory rootScopeFactory = rootContainer.GetRequiredService<IServiceScopeFactory>();

            var jobHostServices = new ServiceCollection();
            jobHostServices.AddScoped<IService, TestService>();
            IServiceProvider serviceProvider = new JobHostServiceProvider(jobHostServices, rootContainer, rootScopeFactory);

            // Create a scope on JobHost container
            IServiceScope scope = serviceProvider.CreateScope();

            // The service resolution should fallback to the parent scope
            TestService rootService = scope.ServiceProvider.GetService<TestService>();

            IService jobHostService = scope.ServiceProvider.GetService<IService>();

            scope.Dispose();

            // Disposing of the JobHost scope should not dispose of root container services
            Assert.False(rootService.Disposed);

            // Disposing of the JobHost scope should dispose of scoped JobHost services
            Assert.True(jobHostService.Disposed);
        }

        [Fact]
        public void Dispose_OnJobHostScope_DisposesRootScopedService()
        {
            var rootServices = new ServiceCollection();
            rootServices.AddScoped<TestService>();
            var rootContainer = rootServices.BuildServiceProvider();

            // Get the root scope factory
            IServiceScopeFactory rootScopeFactory = rootContainer.GetRequiredService<IServiceScopeFactory>();

            IServiceProvider serviceProvider = new JobHostServiceProvider(new ServiceCollection(), rootContainer, rootScopeFactory);

            // Create a scope on JobHost container
            IServiceScope scope = serviceProvider.CreateScope();

            // The service resolution should fallback to the parent scope
            TestService rootService = scope.ServiceProvider.GetService<TestService>();

            scope.Dispose();

            // Disposing of the JobHost scope should also trigger a parent scope disposal
            Assert.True(rootService.Disposed);
        }

        [Fact]
        public void Dispose_OnJobHost_DoesNotDisposRootScopedService()
        {
            var rootServices = new ServiceCollection();
            rootServices.AddSingleton<TestService>();
            var rootContainer = rootServices.BuildServiceProvider();

            // Get the root scope factory
            IServiceScopeFactory rootScopeFactory = rootContainer.GetRequiredService<IServiceScopeFactory>();

            IServiceProvider serviceProvider = new JobHostServiceProvider(new ServiceCollection(), rootContainer, rootScopeFactory);

            // The service resolution should fallback to the parent scope
            TestService rootService = serviceProvider.GetService<TestService>();

            ((IDisposable)serviceProvider).Dispose();

            // Disposing of the JobHost service provider should not dispose of root container services
            Assert.False(rootService.Disposed);
        }

        private class TestService : IService, IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        internal interface IService
        {
            bool Disposed { get; }
        }
    }
}
