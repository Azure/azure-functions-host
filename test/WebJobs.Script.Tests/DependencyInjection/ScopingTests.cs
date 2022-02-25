// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using DryIoc;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.DependencyInjection
{
    public class ScopingTests
    {
        [Fact]
        public void Extensions_DependencyInjection_Scopes()
        {
            // test to make sure we understand how default DI works
            var jobHostServices = new ServiceCollection();
            jobHostServices.AddSingleton(p => new SingletonUsingFactory(p));
            jobHostServices.AddTransient(p => new TransientUsingFactory(p));
            IServiceProvider serviceProvider = jobHostServices.BuildServiceProvider();

            SingletonUsingFactory sFact = null;
            TransientUsingFactory tFact = null;

            using (var scope = serviceProvider.CreateScope())
            {
                sFact = scope.ServiceProvider.GetService<SingletonUsingFactory>();
                tFact = scope.ServiceProvider.GetService<TransientUsingFactory>();
            }

            sFact.DoSomething();
            Assert.Throws<ObjectDisposedException>(() => tFact.DoSomething());
        }

        [Fact]
        public void DryIoc_DependencyInjection_Scopes()
        {
            var rootServices = new ServiceCollection();
            var rootContainer = rootServices.BuildServiceProvider();

            // Get the root scope factory
            IServiceScopeFactory rootScopeFactory = rootContainer.GetRequiredService<IServiceScopeFactory>();

            var jobHostServices = new ServiceCollection();
            jobHostServices.AddSingleton(p => new SingletonUsingFactory(p));
            jobHostServices.AddTransient(p => new TransientUsingFactory(p));
            IServiceProvider serviceProvider = new JobHostServiceProvider(jobHostServices, rootContainer, rootScopeFactory);

            SingletonUsingFactory sFact = null;
            TransientUsingFactory tFact = null;

            using (var scope = serviceProvider.CreateScope())
            {
                sFact = scope.ServiceProvider.GetService<SingletonUsingFactory>();
                tFact = scope.ServiceProvider.GetService<TransientUsingFactory>();
            }

            sFact.DoSomething();
            var ex = Assert.Throws<ContainerException>(() => tFact.DoSomething());
            Assert.Equal(39, ex.Error);
            Assert.StartsWith("Container is disposed", ex.Message);
        }

        private class SingletonUsingFactory : ServiceProviderHolder
        {
            public SingletonUsingFactory(IServiceProvider sp) : base(sp) { }
        }

        private class TransientUsingFactory : ServiceProviderHolder
        {
            public TransientUsingFactory(IServiceProvider sp) : base(sp) { }
        }

        private abstract class ServiceProviderHolder
        {
            private readonly IServiceProvider _serviceProvider;

            public ServiceProviderHolder(IServiceProvider serviceProvider)
            {
                _serviceProvider = serviceProvider;
            }

            public void DoSomething()
            {
                var options = _serviceProvider.GetService<IOptions<ScriptApplicationHostOptions>>();
            }
        }
    }
}
