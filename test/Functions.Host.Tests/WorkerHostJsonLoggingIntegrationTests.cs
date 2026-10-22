// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.Host.Tests;

[Collection(nameof(EnvironmentVariableCollection))]
public sealed class WorkerHostJsonLoggingIntegrationTests
{
    private const string RejectionCategory = "Azure.Functions.Rpc.Client.WorkerHostJsonConfiguration";
    private const string RejectionEvent = "HostJsonConfigurationRejected";
    private const string ConsoleControl = "Root host.json diagnostic console control.";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData((HostJsonConfiguration.Types.Status)5, "The worker host.json delivery status is unspecified or unsupported.")]
    [InlineData(HostJsonConfiguration.Types.Status.ReadError, "The worker could not read host.json.")]
    [InlineData(HostJsonConfiguration.Types.Status.TooLarge, "The worker host.json exceeds the 65,536-byte limit.")]
    [InlineData(HostJsonConfiguration.Types.Status.Available, "The worker host.json exceeds the 65,536-byte limit.")]
    public async Task RejectedConfiguration_UsesRootSystemEventSinkBeforeScriptHostStarts(
        HostJsonConfiguration.Types.Status status, string expectedReason)
    {
        using EnvironmentVariableScope environment = new(EnvironmentSettingNames.EnvironmentNameKey, Environments.Development);
        using SystemLogListener listener = new();
        using StringWriter console = new();
        TextWriter originalConsole = Console.Out;
        string directory = Path.Combine(Path.GetTempPath(), $"WorkerHostJsonLogging-{Guid.NewGuid():N}");
        string scriptPath = Path.Combine(directory, "app");
        Directory.CreateDirectory(scriptPath);
        HostJsonConfiguration delivery = new()
        {
            Status = status,
            Content = status is HostJsonConfiguration.Types.Status.Available
                ? ByteString.CopyFromUtf8("customer-secret".PadRight(65_537, 'x'))
                : ByteString.Empty,
        };
        WorkerService service = new(delivery);
        WebApplicationBuilder workerBuilder = WebApplication.CreateSlimBuilder();
        workerBuilder.Logging.ClearProviders();
        workerBuilder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listener => listener.Protocols = HttpProtocols.Http2));
        workerBuilder.Services.AddSingleton(service);
        workerBuilder.Services.AddGrpc();
        await using WebApplication worker = workerBuilder.Build();
        worker.MapGrpcService<WorkerService>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await worker.StartAsync(timeout.Token);

        try
        {
            Console.SetOut(console);
            await using (HostFactory factory = new(scriptPath, directory))
            {
                IServiceProvider services = factory.Services;
                Assert.Contains(services.GetServices<ILoggerProvider>(), provider => provider is WebHostSystemLoggerProvider);
                Assert.Equal("EtwEventGenerator", services.GetRequiredService<IEventGenerator>().GetType().Name);
                ILoggerFactory loggerFactory = services.GetRequiredService<ILoggerFactory>();
                loggerFactory.CreateLogger("Host.Startup").LogWarning(ConsoleControl);
                IWorkerChannelRegistry registry = services.GetRequiredService<IWorkerChannelRegistry>();
                string endpoint = worker.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

                WorkerLinkException failure = await Assert.ThrowsAsync<WorkerLinkException>(() =>
                    registry.LinkAsync("worker", new Uri(endpoint), timeout.Token));

                Assert.Equal(WorkerLinkFailureReason.Unavailable, failure.Reason);
                Assert.Equal(expectedReason, failure.Message);
                Assert.False(service.InitRequest!.Capabilities.ContainsKey("HostJson"));
                Assert.Empty(registry.GetInitializedChannels());
                Assert.Null(services.GetRequiredService<IScriptHostManager>().Services);
                Assert.Empty(Directory.EnumerateFiles(scriptPath));
                var systemEvent = Assert.Single(listener.Rejections);
                Assert.Equal(EventLevel.Warning, systemEvent.Level);
                Assert.Equal(nameof(FunctionsSystemLogsEventSource.RaiseFunctionsEventWarning), systemEvent.EventName);
                Dictionary<string, object?> log = systemEvent.Payload;
                Assert.Equal(RejectionEvent, log["EventName"]);
                Assert.Equal(RejectionCategory, log["Source"]);
                Assert.Equal($"Worker host.json configuration was rejected. {expectedReason}", log["Summary"]);
                Assert.Equal(string.Empty, log["Details"]);
                Assert.Equal(string.Empty, log["HostInstanceId"]);
                Assert.DoesNotContain(log.Values, value =>
                    value is string text && text.Contains("customer-secret", StringComparison.Ordinal));
            }

            string output = console.ToString();
            Assert.Contains(ConsoleControl, output, StringComparison.Ordinal);
            Assert.DoesNotContain(RejectionCategory, output, StringComparison.Ordinal);
            Assert.DoesNotContain(expectedReason, output, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalConsole);
            await worker.StopAsync(CancellationToken.None).WaitAsync(TestTimeout);
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class HostFactory(string scriptPath, string directory) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(directory);
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureFunctionsWebHost:ScriptPath"] = scriptPath,
                    ["AzureFunctionsWebHost:LogPath"] = Path.Combine(directory, "logs"),
                    ["AzureFunctionsWebHost:SecretsPath"] = Path.Combine(directory, "secrets"),
                    ["AzureFunctionsWebHost:IsSelfHost"] = "true",
                    ["AzureFunctionsWebHost:IsFileSystemReadOnly"] = "true",
                    ["AzureFunctionsJobHost:logging:logLevel:default"] = "Information",
                    ["AzureFunctionsJobHost:logging:console:isEnabled"] = "true",
                    [EnvironmentSettingNames.AzureWebJobsSecretStorageType] = "Files",
                });
            });
        }
    }

    private sealed class SystemLogListener : EventListener
    {
        public ConcurrentQueue<(EventLevel Level, string? EventName, Dictionary<string, object?> Payload)> Rejections { get; } = new();

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (string.Equals(eventSource.Name, nameof(FunctionsSystemLogsEventSource), StringComparison.Ordinal))
            {
                EnableEvents(eventSource, EventLevel.Warning, EventKeywords.All);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventId is 65522 && eventData.PayloadNames is not null && eventData.Payload is not null)
            {
                Dictionary<string, object?> payload = eventData.PayloadNames
                    .Select((name, index) => KeyValuePair.Create(name, eventData.Payload[index]))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                if (payload.GetValueOrDefault("EventName") is string eventName &&
                    string.Equals(eventName, RejectionEvent, StringComparison.Ordinal))
                {
                    Rejections.Enqueue((eventData.Level, eventData.EventName, payload));
                }
            }
        }
    }

    public sealed class WorkerService(HostJsonConfiguration? delivery) : FunctionRpc.FunctionRpcBase
    {
        public WorkerInitRequest? InitRequest { get; private set; }

        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream,
            IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            try
            {
                await responseStream.WriteAsync(new() { StartStream = new() { WorkerId = "worker" } });
                while (await requestStream.MoveNext(context.CancellationToken))
                {
                    StreamingMessage request = requestStream.Current;
                    if (request.ContentCase is not StreamingMessage.ContentOneofCase.WorkerInitRequest)
                    {
                        throw new InvalidOperationException("Only initialization is expected for a rejected worker.");
                    }

                    InitRequest = request.WorkerInitRequest;
                    await responseStream.WriteAsync(new()
                    {
                        RequestId = request.RequestId,
                        WorkerInitResponse = new()
                        {
                            Result = new() { Status = StatusResult.Types.Status.Success },
                            HostJson = delivery,
                        },
                    });
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
