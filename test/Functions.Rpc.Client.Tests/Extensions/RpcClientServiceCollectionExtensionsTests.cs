// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RpcClientServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRpcClientServices_RegistersClientLifecycleAsSingletons()
    {
        ServiceCollection services = new();

        IServiceCollection result = services.AddRpcClientServices();

        Assert.Same(services, result);
        AssertSingleton(services, typeof(IHttpProxyService), "Microsoft.Azure.WebJobs.Script.Http.DefaultHttpProxyService");
        AssertSingleton<IRpcClientFactory, RpcClientFactory>(services);
        AssertSingleton<IDuplexChannelFactory<StreamingMessage>, FunctionRpcDuplexChannelFactory>(services);
        AssertSingleton<IRpcClientWorkerChannelFactory, RpcClientWorkerChannelFactory>(services);
        AssertSingleton<IWorkerChannelRegistry, WorkerChannelRegistry>(services);
        AssertSingleton<IWorkerFunctionMetadataProvider, RpcClientWorkerFunctionMetadataProvider>(services);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(RpcClientScriptHostStartupCoordinator));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => services.AddRpcClientServices());
        Assert.Contains(nameof(RpcClientServiceCollectionExtensions.AddRpcClientServices), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRpcClientWebHostServices_RegistersHostLifecycleWithoutDuplicatingMetadataProvider()
    {
        ServiceCollection services = new();
        services.AddSingleton(Mock.Of<IFunctionMetadataProvider>());
        IHostedService scriptHost = Mock.Of<IHostedService>();

        IServiceCollection result = services.AddRpcClientWebHostServices(_ => scriptHost);

        Assert.Same(services, result);
        AssertSingleton<IWorkerChannelRegistry, WorkerChannelRegistry>(services);
        AssertSingleton<IWorkerFunctionMetadataProvider, RpcClientWorkerFunctionMetadataProvider>(services);
        AssertSingleton<IFunctionMetadataProvider, RpcClientFunctionMetadataProvider>(services);
        ServiceDescriptor coordinator = Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(RpcClientScriptHostStartupCoordinator)));
        Assert.Equal(ServiceLifetime.Singleton, coordinator.Lifetime);
        Assert.NotNull(coordinator.ImplementationFactory);
        ServiceDescriptor hostedService = Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)));
        Assert.Equal(ServiceLifetime.Singleton, hostedService.Lifetime);
        Assert.NotNull(hostedService.ImplementationFactory);
        Assert.Throws<InvalidOperationException>(() => services.AddRpcClientWebHostServices(_ => scriptHost));
    }

    [Fact]
    public void AddRpcClientScriptHostServices_RegistersRootRegistryBeforeChildServices()
    {
        var rootRegistry = new Mock<IWorkerChannelRegistry>(MockBehavior.Strict);
        var metadataProvider = new Mock<IWorkerFunctionMetadataProvider>(MockBehavior.Strict);
        using ServiceProvider rootServiceProvider = new ServiceCollection()
            .AddSingleton(rootRegistry.Object)
            .AddSingleton(metadataProvider.Object)
            .BuildServiceProvider();
        ServiceCollection services = new();

        IServiceCollection result = services.AddRpcClientScriptHostServices(rootServiceProvider);

        Assert.Same(services, result);
        ServiceDescriptor registryDescriptor = Assert.Single(services.Where(service => service.ServiceType == typeof(IWorkerChannelRegistry)));
        Assert.Same(rootRegistry.Object, registryDescriptor.ImplementationInstance);
        ServiceDescriptor metadataDescriptor = Assert.Single(services.Where(service => service.ServiceType == typeof(IWorkerFunctionMetadataProvider)));
        Assert.Same(metadataProvider.Object, metadataDescriptor.ImplementationInstance);
        Assert.True(services.IndexOf(registryDescriptor) <
            services.IndexOf(services.Single(service => service.ServiceType == typeof(IRpcClientFunctionInvocationDispatcher))));
        AssertSingleton<IRpcClientFunctionInvocationDispatcher, RpcClientFunctionInvocationDispatcher>(services);
        AssertSingleton<IFunctionInvocationDispatcherFactory, RpcClientFunctionInvocationDispatcherFactory>(services);
        AssertSingleton<IScriptHostWorkerManager, RpcClientScriptHostWorkerManager>(services);
        AssertSingleton(services, typeof(IWorkerFunctionDescriptorProviderFactory),
            "Microsoft.Azure.WebJobs.Script.Description.RpcWorkerFunctionDescriptorProviderFactory");
        AssertSingleton(services, typeof(IScriptHostLifecycleService),
            "Microsoft.Azure.WebJobs.Script.Rpc.RpcScriptHostLifecycleService");
    }

    [Fact]
    public async Task AddRpcClientScriptHostServices_ChildDisposalDoesNotDisposeRootRegistryOrChannels()
    {
        bool channelDisposed = false;
        var rootRegistry = new Mock<IWorkerChannelRegistry>(MockBehavior.Strict);
        var metadataProvider = new Mock<IWorkerFunctionMetadataProvider>(MockBehavior.Strict);
        rootRegistry.Setup(registry => registry.DisposeAsync())
            .Callback(() => channelDisposed = true)
            .Returns(ValueTask.CompletedTask);
        using ServiceProvider rootServiceProvider = new ServiceCollection()
            .AddSingleton(rootRegistry.Object)
            .AddSingleton(metadataProvider.Object)
            .BuildServiceProvider();
        ServiceCollection services = new();
        services.AddRpcClientScriptHostServices(rootServiceProvider);

        await using (ServiceProvider childServiceProvider = services.BuildServiceProvider())
        {
            Assert.Same(rootRegistry.Object, childServiceProvider.GetRequiredService<IWorkerChannelRegistry>());
        }

        rootRegistry.Verify(registry => registry.DisposeAsync(), Times.Never);
        Assert.False(channelDisposed);
    }

    [Fact]
    public void RegistrationMethods_ValidateArguments()
    {
        ServiceCollection services = new();
        using ServiceProvider rootServiceProvider = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<ArgumentNullException>(() => RpcClientServiceCollectionExtensions.AddRpcClientServices(null));
        Assert.Throws<ArgumentNullException>(() => RpcClientServiceCollectionExtensions.AddRpcClientWebHostServices(null, _ => Mock.Of<IHostedService>()));
        Assert.Throws<ArgumentNullException>(() => services.AddRpcClientWebHostServices(null));
        Assert.Throws<ArgumentNullException>(() =>
            RpcClientServiceCollectionExtensions.AddRpcClientScriptHostServices(null, rootServiceProvider));
        Assert.Throws<ArgumentNullException>(() => services.AddRpcClientScriptHostServices(null));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => services.AddRpcClientScriptHostServices(rootServiceProvider));
        Assert.Contains(nameof(RpcClientServiceCollectionExtensions.AddRpcClientServices), exception.Message, StringComparison.Ordinal);
    }

    private static void AssertSingleton<TService, TImplementation>(IServiceCollection services)
    {
        ServiceDescriptor descriptor = Assert.Single(services.Where(service => service.ServiceType == typeof(TService)));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }

    private static void AssertSingleton(IServiceCollection services, Type serviceType, string implementationTypeName)
    {
        ServiceDescriptor descriptor = Assert.Single(services.Where(service => service.ServiceType == serviceType));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(implementationTypeName, descriptor.ImplementationType?.FullName);
    }
}
