// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RuntimeApplicationAuthenticationOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("invalid-app-key")]
    public async Task HostStartup_DoesNotRequireApplicationProofConfiguration(string appKey)
    {
        using IHost host = new HostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string>
            {
                [RuntimeWorkerAuthentication.AppNameSetting] = "test-app",
                [RuntimeWorkerAuthentication.AppKeySetting] = appKey
            }))
            .ConfigureServices(services => services.AddRpcClientServices())
            .Build();

        await host.StartAsync();
        Assert.Single(RuntimeWorkerTestData.CreateCredentials().CreateHeaders());
        IOptionsFactory<RuntimeApplicationAuthenticationOptions> options = host.Services.GetRequiredService<IOptionsFactory<RuntimeApplicationAuthenticationOptions>>();
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => options.Create(Options.DefaultName));
        Assert.Equal([appKey is null ? "MissingKey" : "InvalidKey"], error.Failures);
        await host.StopAsync();
    }

    [Fact]
    public void FreshProofOptions_ReadLiveValuesOnceAndNeverReadAnotherKeyRoleOrReloadToken()
    {
        string key = RuntimeWorkerTestData.AppKey;
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);
        configuration.SetupGet(value => value[RuntimeWorkerAuthentication.AppNameSetting]).Returns("test-app");
        configuration.SetupGet(value => value[RuntimeWorkerAuthentication.AppKeySetting]).Returns(() => key);
        ServiceCollection services = new();
        services.AddSingleton(configuration.Object);
        services.AddRpcClientServices();
        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsFactory<RuntimeApplicationAuthenticationOptions> options = provider.GetRequiredService<IOptionsFactory<RuntimeApplicationAuthenticationOptions>>();
        RuntimeWorkerCredentials credentials = RuntimeWorkerTestData.CreateCredentials();
        string original = RuntimeWorkerTestData.CreateAppProof("worker");

        Assert.Single(credentials.CreateHeaders());
        configuration.VerifyNoOtherCalls();
        RuntimeApplicationAuthenticationOptions first = options.Create(Options.DefaultName);
        credentials.ValidateAppProof(original, first.AppName, first.AppKey);
        key = Convert.ToHexString(SHA256.HashData("live-replacement-key"u8));
        RuntimeApplicationAuthenticationOptions second = options.Create(Options.DefaultName);
        Assert.NotSame(first, second);
        WorkerLinkException error = Assert.Throws<WorkerLinkException>(() =>
            credentials.ValidateAppProof(original, second.AppName, second.AppKey));
        Assert.Equal(WorkerLinkFailureReason.Unavailable, error.Reason);
        credentials.ValidateAppProof(RuntimeWorkerAuthentication.CreateAppProof(
            Convert.FromHexString(key), "test-app", "worker", DateTimeOffset.UtcNow), second.AppName, second.AppKey);

        configuration.VerifyGet(value => value[RuntimeWorkerAuthentication.AppNameSetting], Times.Exactly(2));
        configuration.VerifyGet(value => value[RuntimeWorkerAuthentication.AppKeySetting], Times.Exactly(2));
        configuration.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, null, "MissingIdentity")]
    [InlineData(" ", "invalid-private-key", "MissingIdentity")]
    [InlineData("test-app", null, "MissingKey")]
    [InlineData("test-app", "invalid-private-key", "InvalidKey")]
    public void ApplicationOptions_ValidateOnlySafeCategoriesAtTheProofBoundary(string appName, string appKey, string reason)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            [RuntimeWorkerAuthentication.AppNameSetting] = appName,
            [RuntimeWorkerAuthentication.AppKeySetting] = appKey,
            [RuntimeWorkerAuthentication.WorkerKeySetting] = RuntimeWorkerTestData.WorkerKey
        }).Build();
        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddRpcClientServices();
        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsFactory<RuntimeApplicationAuthenticationOptions> options = provider.GetRequiredService<IOptionsFactory<RuntimeApplicationAuthenticationOptions>>();

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => options.Create(Options.DefaultName));

        Assert.Equal([reason], error.Failures);
        Assert.DoesNotContain("invalid-private-key", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RuntimeWorkerTestData.WorkerKey, error.Message, StringComparison.Ordinal);
    }
}
