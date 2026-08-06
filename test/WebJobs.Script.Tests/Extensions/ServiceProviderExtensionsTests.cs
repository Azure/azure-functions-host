// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class ServiceProviderExtensionsTests
    {
        [Fact]
        public void GetScriptHostServiceOrNull_ReturnsExpectedValue()
        {
            ITestInterface test = new TestClass();
            Mock<IScriptHostManager> scriptHostManagerMock = new(MockBehavior.Strict);
            Mock<IServiceProvider> scriptHostServiceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            scriptHostServiceProviderMock.Setup(p => p.GetService(typeof(ITestInterface))).Returns(() => test);

            Mock<IServiceProvider> serviceProviderMock = new(MockBehavior.Strict);
            serviceProviderMock.Setup(p => p.GetService(typeof(IScriptHostManager))).Returns(scriptHostManagerMock.Object);

            var result = serviceProviderMock.Object.GetScriptHostServiceOrNull<ITestInterface>();
            result.Should().BeSameAs(test);
        }

        [Fact]
        public void GetScriptHostServiceOrNull_NonScriptHost_ReturnsNull()
        {
            Mock<IServiceProvider> serviceProviderMock = new(MockBehavior.Strict);
            serviceProviderMock.Setup(p => p.GetService(typeof(IScriptHostManager))).Returns(null);

            ITestInterface result = serviceProviderMock.Object.GetScriptHostServiceOrNull<ITestInterface>();
            result.Should().BeNull();
        }

        [Fact]
        public void GetScriptHostServiceOrNull_ContainerDisposed_ReturnsNull()
        {
            Mock<IServiceProvider> serviceProviderMock = new(MockBehavior.Strict);
            serviceProviderMock.Setup(p => p.GetService(typeof(IScriptHostManager))).Throws(new ObjectDisposedException("test"));
            ITestInterface result = serviceProviderMock.Object.GetScriptHostServiceOrNull<ITestInterface>();
            result.Should().BeNull();
        }

        [Fact]
        public void CreateInstance_NullServices_Throws()
        {
            IServiceProvider serviceProviderMock = null!;
            ServiceDescriptor descriptor = ServiceDescriptor.Singleton<ITestInterface, TestClass>();
            TestHelpers.Act(() => serviceProviderMock.CreateInstance(descriptor))
                .Should().ThrowExactly<ArgumentNullException>();
        }

        [Fact]
        public void CreateInstance_NullDescriptor_Throws()
        {
            IServiceProvider serviceProviderMock = Mock.Of<IServiceProvider>();
            TestHelpers.Act(() => serviceProviderMock.CreateInstance(null))
                .Should().ThrowExactly<ArgumentNullException>();
        }

        [Fact]
        public void CreateInstance_ImplementationInstance_Returns()
        {
            TestClass instance = new();
            ServiceCollection services = new();
            services.AddSingleton<ITestInterface>(instance);
            IServiceProvider serviceProvider = services.BuildServiceProvider();
            ServiceDescriptor descriptor = services.Single();

            object result = serviceProvider.CreateInstance(descriptor);

            result.Should().BeSameAs(instance);
        }

        [Fact]
        public void CreateInstance_ImplementationType_Creates()
        {
            ServiceCollection services = new();
            TestClass instance = new();
            services.AddSingleton<ITestInterface, TestClass>();
            services.AddSingleton(instance);
            IServiceProvider serviceProvider = services.BuildServiceProvider();
            ServiceDescriptor descriptor = services.First();

            object result = serviceProvider.CreateInstance(descriptor);

            result.Should().BeSameAs(instance);
        }

        [Fact]
        public void CreateInstance_ImplementationType_RetrievesExisting()
        {
            ServiceCollection services = new();
            services.AddSingleton<ITestInterface, TestClass>();
            IServiceProvider serviceProvider = services.BuildServiceProvider();
            ServiceDescriptor descriptor = services.Single();

            object result = serviceProvider.CreateInstance(descriptor);

            result.Should().BeOfType<TestClass>();
        }

        [Fact]
        public void CreateInstance_ImplementationFactory_Creates()
        {
            ServiceCollection services = new();
            services.AddSingleton<ITestInterface>(_ => new TestClass());
            IServiceProvider serviceProvider = services.BuildServiceProvider();
            ServiceDescriptor descriptor = services.Single();

            object result = serviceProvider.CreateInstance(descriptor);

            result.Should().BeOfType<TestClass>();
        }

        [Fact]
        public void CreateChildContainer_RecreatesChildOwnedOptionsAndSharesRootSingleton()
        {
            ServiceCollection rootServices = new();
            rootServices.AddOptions();
            RootSingleton rootSingleton = new();
            rootServices.AddSingleton(rootSingleton);
            using ServiceProvider rootProvider = rootServices.BuildServiceProvider();
            int generation = 0;

            ServiceProvider BuildChild()
            {
                IServiceCollection childServices = rootProvider.CreateChildContainer(rootServices);
                childServices
                    .AddOptions<ChildOwnedOptions>()
                    .Configure(options => options.Generation = ++generation);

                return childServices.BuildServiceProvider();
            }

            using ServiceProvider firstChild = BuildChild();
            ChildOwnedOptions firstOptions =
                firstChild.GetRequiredService<IOptions<ChildOwnedOptions>>().Value;
            using ServiceProvider secondChild = BuildChild();
            ChildOwnedOptions secondOptions =
                secondChild.GetRequiredService<IOptions<ChildOwnedOptions>>().Value;

            firstOptions.Generation.Should().Be(1);
            secondOptions.Generation.Should().Be(2);
            secondOptions.Should().NotBeSameAs(firstOptions);
            firstChild.GetRequiredService<RootSingleton>().Should().BeSameAs(rootSingleton);
            secondChild.GetRequiredService<RootSingleton>().Should().BeSameAs(rootSingleton);
        }

        private sealed class ChildOwnedOptions
        {
            public int Generation { get; set; }
        }

        private sealed class RootSingleton
        {
        }
    }
}
