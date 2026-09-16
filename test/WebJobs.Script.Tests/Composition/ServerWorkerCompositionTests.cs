// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Composition;

public class ServerWorkerCompositionTests
{
    [Fact]
    public void ConfigureWebHostServices_RegistersExistingStandardServices()
    {
        var expected = new ServiceCollection();
        expected.AddScriptGrpc();
        expected.AddCommonRpcServices();
        expected.AddSingleton<IHostedService>(provider => provider.GetRequiredService<WebJobsScriptHostService>());

        var actual = new ServiceCollection();
        IMvcBuilder mvcBuilder = new ServiceCollection().AddMvc();

        ServerWorkerComposition.Instance.ConfigureWebHostServices(actual, mvcBuilder);

        Assert.Equal(expected.Select(GetDescriptorSignature), actual.Select(GetDescriptorSignature));

        ServiceDescriptor activationDescriptor = actual.Last();
        using ServiceProvider serviceProvider = new ServiceCollection().BuildServiceProvider();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => activationDescriptor.ImplementationFactory(serviceProvider));
        Assert.True(exception.Message.Contains(typeof(WebJobsScriptHostService).FullName, StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigureScriptHostServices_RegistersExistingStandardServices()
    {
        var expected = new ServiceCollection();
        expected.AddRpcScriptHostServices();

        var actual = new ServiceCollection();
        using ServiceProvider rootServiceProvider = new ServiceCollection().BuildServiceProvider();

        ServerWorkerComposition.Instance.ConfigureScriptHostServices(actual, rootServiceProvider);

        Assert.Equal(expected.Select(GetDescriptorSignature), actual.Select(GetDescriptorSignature));
    }

    private static string GetDescriptorSignature(ServiceDescriptor descriptor)
    {
        string implementation = descriptor.ImplementationFactory is not null
            ? "factory"
            : descriptor.ImplementationType?.AssemblyQualifiedName
                ?? descriptor.ImplementationInstance?.GetType().AssemblyQualifiedName
                ?? "unknown";

        return $"{descriptor.ServiceType.AssemblyQualifiedName}|{descriptor.Lifetime}|{implementation}";
    }
}
