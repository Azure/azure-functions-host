// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using DryIoc;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.DependencyInjection
{
    /// <summary>
    /// Set of tests to compare DryIoc IServiceProvider resolution to the default ServiceProvider
    /// </summary>
    public class JobHostServiceProviderScopeTests
    {
        private UsingConstructor _defaultConstructor;
        private UsingFactory _defaultFactory;

        private UsingConstructor _jobHostConstructor;
        private UsingFactory _jobHostFactory;

        [Fact]
        public void JobHostServiceProvider_SingletonInScope()
        {
            RunScopedResolutionTest(ServiceLifetime.Singleton);

            // The captured IServiceProviders should not be disposed, even though they were resolved inside a scope,
            // as they are being resolved by a Singleton service. These should not throw.
            _defaultConstructor.DoSomething();
            _defaultFactory.DoSomething();

            _jobHostConstructor.DoSomething();
            _jobHostFactory.DoSomething();
        }

        [Fact]
        public void JobHostServiceProvider_TransientInScope()
        {
            RunScopedResolutionTest(ServiceLifetime.Transient);

            // These IServiceProviders are all disposed when exiting the scope, so these all throw.
            Assert.Throws<ObjectDisposedException>(() => _defaultConstructor.DoSomething());
            Assert.Throws<ObjectDisposedException>(() => _defaultFactory.DoSomething());

            Assert.Throws<ContainerException>(() => _jobHostConstructor.DoSomething());
            Assert.Throws<ContainerException>(() => _jobHostFactory.DoSomething());
        }

        [Fact]
        public void JobHostServiceProvider_ScopedInScope()
        {
            RunScopedResolutionTest(ServiceLifetime.Scoped);

            // These IServiceProviders are all disposed when exiting the scope, so these all throw.
            Assert.Throws<ObjectDisposedException>(() => _defaultConstructor.DoSomething());
            Assert.Throws<ObjectDisposedException>(() => _defaultFactory.DoSomething());

            Assert.Throws<ContainerException>(() => _jobHostConstructor.DoSomething());
            Assert.Throws<ContainerException>(() => _jobHostFactory.DoSomething());
        }

        [Fact]
        public void JobHostServiceProvider_SingletonAndScoped()
        {
            // Build JobHostServiceProvider
            var rootServices = new ServiceCollection();
            var rootContainer = rootServices.BuildServiceProvider();
            IServiceScopeFactory rootScopeFactory = rootContainer.GetRequiredService<IServiceScopeFactory>();
            var jobHostServices = new ServiceCollection();

            jobHostServices.AddSingleton<SingletonConstructor>();
            jobHostServices.AddSingleton<SingletonFactory>(p => new SingletonFactory(p));

            jobHostServices.AddScoped<ScopedConstructor>();
            jobHostServices.AddScoped<ScopedFactory>(p => new ScopedFactory(p));

            IServiceProvider jobHostProvider = new JobHostServiceProvider(jobHostServices, rootContainer, rootScopeFactory);

            // Resolve some root services
            SingletonConstructor rootSingletonConstructor = jobHostProvider.GetService<SingletonConstructor>();
            SingletonFactory rootSingletonFactory = jobHostProvider.GetService<SingletonFactory>();
            ScopedConstructor rootScopedConstructor = jobHostProvider.GetService<ScopedConstructor>();
            ScopedFactory rootScopedFactory = jobHostProvider.GetService<ScopedFactory>();

            // Resolve some child scope services
            SingletonConstructor childSingletonConstructor1;
            SingletonFactory childSingletonFactory1;
            ScopedConstructor childScopedConstructor1;
            ScopedFactory childScopedFactory1;

            using (var scope = jobHostProvider.CreateScope())
            {
                childSingletonConstructor1 = scope.ServiceProvider.GetService<SingletonConstructor>();
                childSingletonFactory1 = scope.ServiceProvider.GetService<SingletonFactory>();

                childScopedConstructor1 = scope.ServiceProvider.GetService<ScopedConstructor>();
                childScopedFactory1 = scope.ServiceProvider.GetService<ScopedFactory>();
            }

            // Do it again
            SingletonConstructor childSingletonConstructor2;
            SingletonFactory childSingletonFactory2;
            ScopedConstructor childScopedConstructor2;
            ScopedFactory childScopedFactory2;

            using (var scope = jobHostProvider.CreateScope())
            {
                childSingletonConstructor2 = scope.ServiceProvider.GetService<SingletonConstructor>();
                childSingletonFactory2 = scope.ServiceProvider.GetService<SingletonFactory>();

                childScopedConstructor2 = scope.ServiceProvider.GetService<ScopedConstructor>();
                childScopedFactory2 = scope.ServiceProvider.GetService<ScopedFactory>();
            }

            // Ensure all ServiceProviders for Singletons are the same.
            Assert.Same(rootSingletonConstructor.ServiceProvider, childSingletonConstructor1.ServiceProvider);
            Assert.Same(childSingletonConstructor1.ServiceProvider, rootSingletonFactory.ServiceProvider);
            Assert.Same(rootSingletonFactory.ServiceProvider, childSingletonFactory1.ServiceProvider);

            // The root ServiceProviders for Scoped should also be the same.
            Assert.Same(childSingletonFactory1.ServiceProvider, rootScopedConstructor.ServiceProvider);
            Assert.Same(rootScopedConstructor.ServiceProvider, rootScopedFactory.ServiceProvider);

            // Root and child should not match for Scoped
            Assert.NotSame(rootScopedConstructor.ServiceProvider, childScopedConstructor1.ServiceProvider);
            Assert.NotSame(rootScopedFactory.ServiceProvider, childScopedFactory1.ServiceProvider);

            // Both Scopes should match
            Assert.Same(childScopedConstructor1.ServiceProvider, childScopedFactory1.ServiceProvider);
            Assert.Same(childScopedConstructor2.ServiceProvider, childScopedFactory2.ServiceProvider);

            // But not between scopes
            Assert.NotSame(childScopedConstructor1.ServiceProvider, childScopedConstructor2.ServiceProvider);
            Assert.NotSame(childScopedFactory1.ServiceProvider, childScopedFactory2.ServiceProvider);

            // Roots and Singletons are not disposed
            rootSingletonConstructor.DoSomething();
            rootSingletonFactory.DoSomething();
            rootScopedConstructor.DoSomething();
            rootScopedFactory.DoSomething();
            childSingletonConstructor1.DoSomething();
            childSingletonFactory1.DoSomething();
            childSingletonConstructor2.DoSomething();
            childSingletonFactory2.DoSomething();

            // Child scopes are disposed.
            Assert.Throws<ContainerException>(() => childScopedConstructor1.DoSomething());
            Assert.Throws<ContainerException>(() => childScopedFactory1.DoSomething());
            Assert.Throws<ContainerException>(() => childScopedConstructor2.DoSomething());
            Assert.Throws<ContainerException>(() => childScopedFactory2.DoSomething());
        }

        private static (IServiceProvider Default, IServiceProvider JobHost) SetupProviders(params ServiceLifetime[] lifetimes)
        {
            // Build ServiceProvider
            var defaultServices = new ServiceCollection();
            foreach (var lifetime in lifetimes)
            {
                defaultServices.Add(new ServiceDescriptor(typeof(UsingConstructor), typeof(UsingConstructor), lifetime));
                defaultServices.Add(new ServiceDescriptor(typeof(UsingFactory), p => new UsingFactory(p), lifetime));
            }
            IServiceProvider defaultProvider = defaultServices.BuildServiceProvider();

            // Build JobHostServiceProvider
            var rootServices = new ServiceCollection();
            var rootContainer = rootServices.BuildServiceProvider();
            IServiceScopeFactory rootScopeFactory = rootContainer.GetRequiredService<IServiceScopeFactory>();
            var jobHostServices = new ServiceCollection();
            foreach (var lifetime in lifetimes)
            {
                jobHostServices.Add(new ServiceDescriptor(typeof(UsingConstructor), typeof(UsingConstructor), lifetime));
                jobHostServices.Add(new ServiceDescriptor(typeof(UsingFactory), p => new UsingFactory(p), lifetime));
            }
            IServiceProvider jobHostProvider = new JobHostServiceProvider(jobHostServices, rootContainer, rootScopeFactory);

            return (defaultProvider, jobHostProvider);
        }

        private void RunScopedResolutionTest(ServiceLifetime lifetime)
        {
            var result = SetupProviders(lifetime);

            // Resolve defaults
            using (var scope = result.Default.CreateScope())
            {
                _defaultConstructor = scope.ServiceProvider.GetService<UsingConstructor>();
                _defaultFactory = scope.ServiceProvider.GetService<UsingFactory>();
            }

            // Resolve JobHost
            using (var scope = result.JobHost.CreateScope())
            {
                _jobHostConstructor = scope.ServiceProvider.GetService<UsingConstructor>();
                _jobHostFactory = scope.ServiceProvider.GetService<UsingFactory>();
            }
        }

        private class UsingConstructor : ServiceProviderHolder
        {
            public UsingConstructor(IServiceProvider sp) : base(sp) { }
        }

        private class UsingFactory : ServiceProviderHolder
        {
            public UsingFactory(IServiceProvider sp) : base(sp) { }
        }

        private class SingletonConstructor : ServiceProviderHolder
        {
            public SingletonConstructor(IServiceProvider sp) : base(sp) { }
        }

        private class SingletonFactory : ServiceProviderHolder
        {
            public SingletonFactory(IServiceProvider sp) : base(sp) { }
        }

        private class ScopedConstructor : ServiceProviderHolder
        {
            public ScopedConstructor(IServiceProvider sp) : base(sp) { }
        }

        private class ScopedFactory : ServiceProviderHolder
        {
            public ScopedFactory(IServiceProvider sp) : base(sp) { }
        }

        private abstract class ServiceProviderHolder
        {
            public ServiceProviderHolder(IServiceProvider serviceProvider)
            {
                ServiceProvider = serviceProvider;
            }

            public IServiceProvider ServiceProvider { get; }

            public void DoSomething()
            {
                var options = ServiceProvider.GetService<IOptions<ScriptApplicationHostOptions>>();
            }
        }
    }
}
