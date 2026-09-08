// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RpcClientServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRpcClientServices_RegistersClientLifecycleAsSingletons()
    {
        ServiceCollection services = new();

        IServiceCollection result = services.AddRpcClientServices();
        services.AddRpcClientServices();

        Assert.Same(services, result);
        AssertSingleton<IRpcClientFactory, RpcClientFactory>(services);
        AssertSingleton<IDuplexChannelFactory<StreamingMessage>, FunctionRpcDuplexChannelFactory>(services);
        AssertSingleton<IRpcClientWorkerChannelFactory, RpcClientWorkerChannelFactory>(services);
        AssertSingleton<IWorkerChannelRegistry, WorkerChannelRegistry>(services);
    }

    [Fact]
    public void AddRpcClientScriptHostServices_RegistersDispatcherFactoryAsSingleton()
    {
        ServiceCollection services = new();

        IServiceCollection result = services.AddRpcClientScriptHostServices();
        services.AddRpcClientScriptHostServices();

        Assert.Same(services, result);
        AssertSingleton<IRpcClientFunctionInvocationDispatcher, RpcClientFunctionInvocationDispatcher>(services);
        AssertSingleton<IFunctionInvocationDispatcherFactory, RpcClientFunctionInvocationDispatcherFactory>(services);
    }

    private static void AssertSingleton<TService, TImplementation>(IServiceCollection services)
    {
        ServiceDescriptor descriptor = Assert.Single(services.Where(service => service.ServiceType == typeof(TService)));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }
}
