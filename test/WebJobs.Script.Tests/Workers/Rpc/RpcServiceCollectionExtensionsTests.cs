// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc;

public class RpcServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(true, typeof(JobObjectRegistry))]
    [InlineData(false, typeof(EmptyProcessRegistry))]
    public void AddProcessRegistry_SelectsImplementationFromProcessPlatform(
        bool isWindows, Type expectedRegistryType)
    {
        ServiceCollection services = new();
        TestProcessFacts processFacts = new(
            isWindows ? OSPlatform.Windows : OSPlatform.Linux,
            Architecture.X64,
            true,
            1);

        RpcServiceCollectionExtensions.AddProcessRegistry(services, processFacts);

        ServiceDescriptor descriptor = Assert.Single(
            services.Where(service => service.ServiceType == typeof(IProcessRegistry)));
        Assert.Equal(expectedRegistryType, descriptor.ImplementationType);
    }
}
