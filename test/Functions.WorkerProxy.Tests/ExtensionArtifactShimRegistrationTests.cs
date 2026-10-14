// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.ExtensionArtifacts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class ExtensionArtifactShimRegistrationTests
{
    [Fact]
    public async Task ApplicationRegistration_UsesSingletonShim()
    {
        await using WorkerProxyWebApplicationFactory webApplicationFactory = new();

        IExtensionArtifactShim firstArtifactShim = webApplicationFactory.Services.GetRequiredService<IExtensionArtifactShim>();
        IExtensionArtifactShim secondArtifactShim = webApplicationFactory.Services.GetRequiredService<IExtensionArtifactShim>();

        Assert.IsType<ExtensionArtifactShim>(firstArtifactShim);
        Assert.Same(firstArtifactShim, secondArtifactShim);
    }
}
