// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;
using HostFunctionDescriptor = Microsoft.Azure.WebJobs.Host.Protocols.FunctionDescriptor;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class WorkerGatedListenerDecoratorTests : IDisposable
{
    private readonly Mock<IFunctionMetadataManager> _metadataManager = new();
    private readonly WorkerGatedListenerManager _manager =
        new(Mock.Of<IWorkerChannelRegistry>(), new FakeTimeProvider(), NullLogger<WorkerGatedListenerManager>.Instance);

    public void Dispose() => _manager.Dispose();

    [Theory]
    [InlineData("""{"type":"queueTrigger"}""", true, false)]
    [InlineData("""{"type":"QueueTrigger"}""", true, false)]
    [InlineData("""{"type":"eventHubTrigger"}""", true, false)]
    [InlineData("""{"type":"cosmosDBTrigger"}""", true, false)]
    [InlineData("""{"type":"kafkaTrigger"}""", true, false)]
    [InlineData("""{"type":"sqlTrigger"}""", true, false)]
    [InlineData("""{"type":"mysqlTrigger"}""", true, false)]
    [InlineData("""{"type":"timerTrigger"}""", true, false)]
    [InlineData("""{"type":"connectorTrigger","deliveryMode":"Poll"}""", true, false)]
    [InlineData("""{"type":"connectorTrigger","DeliveryMode":"poll"}""", true, false)]
    [InlineData("""{"type":"connectorTrigger","deliveryMode":"Webhook"}""", false, false)]
    [InlineData("""{"type":"connectorTrigger"}""", false, false)]
    [InlineData("""{"type":"blobTrigger","source":"EventGrid"}""", true, true)]
    [InlineData("""{"type":"blobTrigger"}""", false, false)]
    [InlineData("""{"type":"httpTrigger"}""", false, false)]
    [InlineData("""{"type":"eventGridTrigger"}""", false, false)]
    [InlineData("""{"type":"serviceBusTrigger"}""", false, false)]
    [InlineData("""{"type":"redisListTrigger"}""", false, false)]
    [InlineData("""{"type":"orchestrationTrigger"}""", false, false)]
    public void IsGated_ClassifiesTriggers(string binding, bool expectedGated, bool expectedStartOnly)
    {
        bool gated = WorkerGatedListenerDecorator.IsGated(CreateBinding(binding), out bool startOnly);

        Assert.Equal(expectedGated, gated);
        Assert.Equal(expectedStartOnly, startOnly);
    }

    [Fact]
    public void Decorate_GatedTrigger_WrapsListenerWithFunctionGroup()
    {
        FunctionMetadata metadata = CreateMetadata("QueueFunction", """{"type":"queueTrigger","direction":"in","name":"message"}""");
        IListener inner = Mock.Of<IListener>();

        IListener result = CreateDecorator().Decorate(CreateContext("QueueFunction", inner));

        WorkerGatedListener gated = Assert.IsType<WorkerGatedListener>(result);
        Assert.Equal("QueueFunction", gated.FunctionName);
        Assert.Equal(metadata.GetFunctionGroup(), gated.FunctionGroup);
    }

    [Fact]
    public async Task Decorate_TimerTrigger_CancelsAnInProgressStart()
    {
        CreateMetadata("TimerFunction", """{"type":"TimerTrigger","direction":"in","name":"timer"}""");
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IListener> inner = new();
        inner.Setup(l => l.StartAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            started.TrySetResult();
            return finishStart.Task;
        });
        inner.Setup(l => l.Cancel()).Callback(() => canceled.TrySetResult());
        using WorkerGatedListener listener = Assert.IsType<WorkerGatedListener>(
            CreateDecorator().Decorate(CreateContext("TimerFunction", inner.Object)));
        using CancellationTokenSource cancellation = new();
        await listener.StartAsync(CancellationToken.None);

        Task<bool> start = listener.StartListeningAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        finishStart.SetResult();

        Assert.False(await start.WaitAsync(TimeSpan.FromSeconds(10)));
        inner.Verify(l => l.Cancel(), Times.Once);
    }

    [Fact]
    public void Decorate_EventGridBlob_UnsupportedListenerFailsBeforeStartup()
    {
        CreateMetadata("BlobFunction", """{"type":"blobTrigger","source":"EventGrid","direction":"in","name":"blob"}""");
        Mock<IListener> inner = new();

        Assert.Throws<NotSupportedException>(() => CreateDecorator().Decorate(CreateContext("BlobFunction", inner.Object)));

        inner.Verify(listener => listener.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Decorate_UngatedTrigger_ReturnsListener()
    {
        CreateMetadata("HttpFunction", """{"type":"httpTrigger","direction":"in","name":"req"}""");
        IListener inner = Mock.Of<IListener>();

        Assert.Same(inner, CreateDecorator().Decorate(CreateContext("HttpFunction", inner)));
    }

    [Fact]
    public void Decorate_UnknownFunction_ReturnsListener()
    {
        IListener inner = Mock.Of<IListener>();

        Assert.Same(inner, CreateDecorator().Decorate(CreateContext("Missing", inner)));
    }

    private WorkerGatedListenerDecorator CreateDecorator()
        => new(_metadataManager.Object, _manager, new FakeTimeProvider(), NullLoggerFactory.Instance);

    private FunctionMetadata CreateMetadata(string name, string trigger)
    {
        FunctionMetadata metadata = new() { Name = name, Language = "external" };
        metadata.Bindings.Add(CreateBinding(trigger));
        _metadataManager.Setup(m => m.TryGetFunctionMetadata(name, out metadata, false)).Returns(true);
        return metadata;
    }

    private static BindingMetadata CreateBinding(string json)
    {
        JObject raw = JObject.Parse(json);
        raw["direction"] ??= "in";
        raw["name"] ??= "trigger";
        return BindingMetadata.Create(raw);
    }

    private static ListenerDecoratorContext CreateContext(string functionName, IListener listener)
    {
        Mock<IFunctionDefinition> definition = new();
        definition.SetupGet(d => d.Descriptor).Returns(new HostFunctionDescriptor { LogName = functionName });
        definition.SetupGet(d => d.ListenerFactory).Returns(Mock.Of<IListenerFactory>());
        return new(definition.Object, listener.GetType(), listener);
    }
}
