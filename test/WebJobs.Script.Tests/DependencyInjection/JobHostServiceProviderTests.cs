// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.DependencyInjection
{
    public class JobHostServiceProviderTests
    {
        private delegate object ServiceFactory(Type type);

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

        [Fact]
        public void Scopes_ChildScopeIsIsolated()
        {
            var services = new ServiceCollection();
            services.AddScoped<A>();

            var rootScopeFactory = new WebHostServiceProvider(new ServiceCollection());
            var jobHostProvider = new JobHostServiceProvider(services, rootScopeFactory, rootScopeFactory);

            var a1 = jobHostProvider.GetService<A>();
            jobHostProvider.CreateScope();
            var a2 = jobHostProvider.GetService<A>();
            Assert.NotNull(a1);
            Assert.NotNull(a2);
            Assert.Same(a1, a2);
        }

        [Fact]
        public void Scopes_Factories()
        {
            IList<IServiceProvider> serviceProviders = new List<IServiceProvider>();

            var services = new ServiceCollection();
            services.AddTransient<A>(p =>
            {
                serviceProviders.Add(p);
                return new A();
            });

            var rootScopeFactory = new WebHostServiceProvider(new ServiceCollection());
            var jobHostProvider = new JobHostServiceProvider(services, rootScopeFactory, rootScopeFactory);

            // Get this service twice.
            // The IServiceProvider passed to the factory should be different because they are separate scopes.
            var scope1 = jobHostProvider.CreateScope();
            scope1.ServiceProvider.GetService<A>();

            var scope2 = jobHostProvider.CreateScope();
            scope2.ServiceProvider.GetService<A>();

            Assert.Equal(2, serviceProviders.Count);
            Assert.NotSame(serviceProviders[0], serviceProviders[1]);
        }

        [Fact]
        public void Scopes_DelegateFactory()
        {
            var services = new ServiceCollection();

            services.AddScoped<A>();
            services.AddScoped<ServiceFactory>(provider => (type) => provider.GetRequiredService(type));

            var rootScopeFactory = new WebHostServiceProvider(new ServiceCollection());
            var jobHostProvider = new JobHostServiceProvider(services, rootScopeFactory, rootScopeFactory);

            var scope1 = jobHostProvider.CreateScope();
            var a1 = scope1.ServiceProvider.GetService<ServiceFactory>()(typeof(A));

            var scope2 = jobHostProvider.CreateScope();
            var a2 = scope2.ServiceProvider.GetService<ServiceFactory>()(typeof(A));

            Assert.NotNull(a1);
            Assert.NotNull(a2);
            Assert.NotSame(a1, a2);
        }

        private class A
        {
            public A()
            {
            }
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
