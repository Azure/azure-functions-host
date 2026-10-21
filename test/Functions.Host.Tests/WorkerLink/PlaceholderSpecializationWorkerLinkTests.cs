// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests.WorkerLink;

// Specialization completes a process-wide standby change token once, so this flow is covered by a single test.
[Collection(nameof(EnvironmentVariableCollection))]
public sealed class PlaceholderSpecializationWorkerLinkTests
{
    private const string WorkerId = "worker-pod-1";
    private const string GrpcEndpoint = "http://100.64.1.12:50053";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task LinkWorker_DuringPlaceholderSpecialization_LinksWithSpecializedConfiguration()
    {
        using EnvironmentVariableScope placeholderMode = new(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
        using EnvironmentVariableScope containerReady = new(EnvironmentSettingNames.AzureWebsiteContainerReady, null);
        IOptionsMonitor<ScriptApplicationHostOptions>? hostOptions = null;
        bool? linkedWithStandbyConfiguration = null;
        Mock<IWorkerChannelRegistry> registry = new();
        registry.Setup(value => value.LinkAsync(WorkerId, new Uri(GrpcEndpoint), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                linkedWithStandbyConfiguration = hostOptions!.CurrentValue.IsStandbyConfiguration;
                return Task.FromResult(new WorkerLinkResult(null!, IsNewLink: true));
            });

        // No channel initializes, so the script host never starts. In compute separation mode, specialization must not
        // wait for the script host, because the script host waits for this link.
        registry.Setup(value => value.WaitForFirstInitializedAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => new TaskCompletionSource<WorkerChannel>().Task.WaitAsync(token));
        registry.Setup(value => value.GetInitializedChannels()).Returns(Array.Empty<WorkerChannel>());

        using IHost host = CreateHost(registry.Object);
        await host.StartAsync();
        try
        {
            hostOptions = host.Services.GetRequiredService<IOptionsMonitor<ScriptApplicationHostOptions>>();
            Assert.True(hostOptions.CurrentValue.IsStandbyConfiguration);
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");

            using CancellationTokenSource timeout = new(TestTimeout);
            using HttpResponseMessage response = await PutLinkAsync(host.GetTestClient(), timeout.Token);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.False(linkedWithStandbyConfiguration);
        }
        finally
        {
            using CancellationTokenSource stopTimeout = new(TimeSpan.FromSeconds(5));
            await host.StopAsync(stopTimeout.Token);
        }
    }

    private static IHost CreateHost(IWorkerChannelRegistry registry)
        => new HostBuilder()
            .ConfigureAppConfiguration(config => config.AddEnvironmentVariables())
            .ConfigureWebHost(webBuilder => webBuilder
                .UseTestServer()
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices((context, services) =>
                {
                    services.AddWebJobsScriptHostAuthentication();
                    services.AddWebJobsScriptHostAuthorization();
                    services.AddWebJobsScriptHost(context.Configuration, ClientWorkerComposition.Instance);
                    services.Replace(ServiceDescriptor.Singleton(registry));
                })
                .Configure(app => app.UseWebJobsScriptHost()))
            .Build();

    private static async Task<HttpResponseMessage> PutLinkAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, $"/admin/workers/{WorkerId}")
        {
            Content = new StringContent($$"""{"workerGrpcEndpoint":"{{GrpcEndpoint}}"}""", Encoding.UTF8, "application/json"),
        };

        return await client.SendAsync(request, cancellationToken);
    }
}
