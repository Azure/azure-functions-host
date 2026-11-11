// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Frozen;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Wraps scaled, pull-based trigger listeners so they run only while a worker serves their function group.
/// </summary>
/// <remarks>
/// Push-based triggers such as HTTP and Event Grid are monitored outside the Host and are left unchanged. Event Grid
/// Blob listeners share one queue listener that cannot restart after a stop, so they are started on demand but never
/// stopped by worker availability. Their shared queue pump is paused instead.
/// </remarks>
internal sealed partial class WorkerGatedListenerDecorator(
    IFunctionMetadataManager metadataManager,
    WorkerGatedListenerManager manager,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory) : IListenerDecorator
{
    private const string ConnectorTrigger = "connectorTrigger";
    private const string ConnectorDeliveryModeProperty = "deliveryMode";
    private const string ConnectorPollDeliveryMode = "Poll";

    private static readonly FrozenSet<string> GatedTriggerTypes = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "queueTrigger",
        "eventHubTrigger",
        "cosmosDBTrigger",
        "kafkaTrigger",
        "sqlTrigger",
        "mysqlTrigger",
        "timerTrigger");

    private readonly IFunctionMetadataManager _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
    private readonly WorkerGatedListenerManager _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger _listenerLogger = loggerFactory?.CreateLogger<WorkerGatedListener>() ?? throw new ArgumentNullException(nameof(loggerFactory));
    private readonly ILogger _logger = loggerFactory.CreateLogger<WorkerGatedListenerDecorator>();

    public IListener Decorate(ListenerDecoratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string functionName = context.FunctionDefinition.Descriptor.LogName;
        if (!_metadataManager.TryGetFunctionMetadata(functionName, out FunctionMetadata metadata))
        {
            Log.MetadataNotFound(_logger, functionName);
            return context.Listener;
        }

        BindingMetadata trigger = metadata.Trigger;
        bool startOnly = false;
        if (trigger is null || !IsGated(trigger, out startOnly))
        {
            return context.Listener;
        }

        string functionGroup = RpcClientInvocationScheduler.GetFunctionGroup(metadata);
        BlobListenerQueueGate blobQueueGate = startOnly ? BlobListenerQueueGate.Install(context.Listener, _listenerLogger) : null;
        Log.GatingListener(_logger, metadata.Name, trigger.Type, functionGroup);
        return new WorkerGatedListener(context.Listener, context.FunctionDefinition.ListenerFactory, metadata.Name, functionGroup, startOnly,
            _manager, _timeProvider, _listenerLogger,
            cancelDuringStart: string.Equals(trigger.Type, "timerTrigger", StringComparison.OrdinalIgnoreCase), blobQueueGate: blobQueueGate);
    }

    internal static bool IsGated(BindingMetadata trigger, out bool startOnly)
    {
        startOnly = false;
        if (GatedTriggerTypes.Contains(trigger.Type))
        {
            return true;
        }

        if (string.Equals(trigger.Type, ConnectorTrigger, StringComparison.OrdinalIgnoreCase))
        {
            // Webhook delivery, the default, is push-based; only polling is scaled by worker demand.
            return trigger.Raw is not null
                && trigger.Raw.TryGetValue(ConnectorDeliveryModeProperty, StringComparison.OrdinalIgnoreCase, out JToken deliveryMode)
                && deliveryMode is { Type: JTokenType.String }
                && string.Equals(deliveryMode.ToString(), ConnectorPollDeliveryMode, StringComparison.OrdinalIgnoreCase);
        }

        if (trigger.IsEventGridBlobTrigger())
        {
            startOnly = true;
            return true;
        }

        return false;
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "Unable to find function metadata for function '{FunctionName}'. Its listener will not be gated by worker availability.")]
        public static partial void MetadataNotFound(ILogger logger, string functionName);

        [LoggerMessage(1, LogLevel.Debug, "Gating the '{TriggerType}' listener for function '{FunctionName}' on workers for group '{FunctionGroup}'.")]
        public static partial void GatingListener(ILogger logger, string functionName, string triggerType, string functionGroup);
    }
}
