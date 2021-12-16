// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.DependencyInjection
{
    public class JobHostScopedServiceProviderFactoryTests
    {
        private delegate object ServiceFactory(Type type);

        [Fact]
        public void Dispose_OnJobHostScope_DoesNotDisposeRootSingletonService()
        {
            // Setup the root
            var rootServices = new ServiceCollection();
            rootServices.AddSingleton<TestService>();
            var rootProvider = rootServices.BuildServiceProvider();
            var mockDepValidator = new Mock<IDependencyValidator>();

            var jobHostServices = new ServiceCollection();
            jobHostServices.AddScoped<IService, TestService>();

            // Then spin up the per-JobHost provider
            var jobHostServiceProviderFactory = new JobHostScopedServiceProviderFactory(rootProvider, rootServices, mockDepValidator.Object);
            IServiceProvider serviceProvider = jobHostServiceProviderFactory.CreateServiceProvider(jobHostServices);

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
            // Setup the root
            var rootServices = new ServiceCollection();
            rootServices.AddScoped<TestService>();
            var rootProvider = rootServices.BuildServiceProvider();
            var mockDepValidator = new Mock<IDependencyValidator>();

            // Then spin up the per-JobHost provider
            var jobHostServiceProviderFactory = new JobHostScopedServiceProviderFactory(rootProvider, rootServices, mockDepValidator.Object);
            IServiceProvider serviceProvider = jobHostServiceProviderFactory.CreateServiceProvider(new ServiceCollection());

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
            // Setup the root
            var rootServices = new ServiceCollection();
            rootServices.AddSingleton<TestService>();
            var rootProvider = rootServices.BuildServiceProvider();
            var mockDepValidator = new Mock<IDependencyValidator>();

            // Then spin up the per-JobHost provider
            var jobHostServiceProviderFactory = new JobHostScopedServiceProviderFactory(rootProvider, rootServices, mockDepValidator.Object);
            IServiceProvider serviceProvider = jobHostServiceProviderFactory.CreateServiceProvider(new ServiceCollection());

            // The service resolution should fallback to the parent scope
            TestService rootService = serviceProvider.GetService<TestService>();

            ((IDisposable)serviceProvider).Dispose();

            // Disposing of the JobHost service provider should not dispose of root container services
            Assert.False(rootService.Disposed);
        }

        [Fact]
        public void Scopes_ChildScopeIsIsolated()
        {
            // Setup the root
            var rootServices = new ServiceCollection();
            rootServices.AddScoped<A>();
            var rootProvider = new ServiceCollection().BuildServiceProvider();
            var mockDepValidator = new Mock<IDependencyValidator>();

            // Then spin up the per-JobHost provider
            var jobHostServiceProviderFactory = new JobHostScopedServiceProviderFactory(rootProvider, rootServices, mockDepValidator.Object);
            IServiceProvider jobServiceProvider = jobHostServiceProviderFactory.CreateServiceProvider(new ServiceCollection());

            var a1 = jobServiceProvider.GetService<A>();
            jobServiceProvider.CreateScope();
            var a2 = jobServiceProvider.GetService<A>();
            Assert.NotNull(a1);
            Assert.NotNull(a2);
            Assert.Same(a1, a2);
        }

        [Fact]
        public void Scopes_Factories()
        {
            // Setup the root
            IList<IServiceProvider> serviceProviders = new List<IServiceProvider>();

            var rootServices = new ServiceCollection();
            rootServices.AddTransient<A>(p =>
            {
                serviceProviders.Add(p);
                return new A();
            });

            var rootProvider = new ServiceCollection().BuildServiceProvider();
            var mockDepValidator = new Mock<IDependencyValidator>();

            // Then spin up the per-JobHost provider
            var jobHostServiceProviderFactory = new JobHostScopedServiceProviderFactory(rootProvider, rootServices, mockDepValidator.Object);
            IServiceProvider jobServiceProvider = jobHostServiceProviderFactory.CreateServiceProvider(new ServiceCollection());

            // Get this service twice.
            // The IServiceProvider passed to the factory should be different because they are separate scopes.
            var scope1 = jobServiceProvider.CreateScope();
            scope1.ServiceProvider.GetService<A>();

            var scope2 = jobServiceProvider.CreateScope();
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

            var rootServices = new ServiceCollection();
            var rootProvider = rootServices.BuildServiceProvider();
            var mockDepValidator = new Mock<IDependencyValidator>();

            // Then spin up the per-JobHost provider
            var jobHostServiceProviderFactory = new JobHostScopedServiceProviderFactory(rootProvider, rootServices, mockDepValidator.Object);
            IServiceProvider jobServiceProvider = jobHostServiceProviderFactory.CreateServiceProvider(services);

            var scope1 = jobServiceProvider.CreateScope();
            var a1 = scope1.ServiceProvider.GetService<ServiceFactory>()(typeof(A));

            var scope2 = jobServiceProvider.CreateScope();
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
