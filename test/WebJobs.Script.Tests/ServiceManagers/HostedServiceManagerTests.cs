// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ServiceManagers
{
    public class HostedServiceManagerTests
    {
        private readonly IEnumerable<IManagedHostedService> managedHostedServices;
        private readonly IHostedService hostedServiceManager;
        private readonly Mock<IRpcServer> mockRpcServer;

        public HostedServiceManagerTests()
        {
            mockRpcServer = new Mock<IRpcServer>();
            managedHostedServices = new List<IManagedHostedService> { new RandomHostedService(mockRpcServer.Object) };
            hostedServiceManager = new HostedServiceManager(managedHostedServices);
        }

        [Fact]
        public async Task HostedServiceManager_Triggers_StopServicesAsync()
        {
            mockRpcServer.Setup(a => a.ShutdownAsync()).Returns(Task.CompletedTask);
            await hostedServiceManager.StopAsync(CancellationToken.None);
            mockRpcServer.Verify(a => a.ShutdownAsync(), Times.Once);
        }
    }
}

internal class RandomHostedService : IManagedHostedService
{
    private readonly IRpcServer rpcServer;

    public RandomHostedService(IRpcServer rpcServer)
    {
        this.rpcServer = rpcServer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task OuterStopAsync(CancellationToken cancellationToken)
    {
        await rpcServer.ShutdownAsync();
    }
}