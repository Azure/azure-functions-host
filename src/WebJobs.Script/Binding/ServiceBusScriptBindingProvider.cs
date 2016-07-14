// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    [CLSCompliant(false)]
    public class ServiceBusScriptBindingProvider : ScriptBindingProvider
    {
        private readonly EventHubConfiguration _eventHubConfiguration;

        public ServiceBusScriptBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter)
            : base(config, hostMetadata, traceWriter)
        {
            _eventHubConfiguration = new EventHubConfiguration();
        }

        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            binding = null;

            if (string.Compare(context.Type, "serviceBusTrigger", StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(context.Type, "serviceBus", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new ServiceBusScriptBinding(context);
            }
            if (string.Compare(context.Type, "eventHubTrigger", StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(context.Type, "eventHub", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new EventHubScriptBinding(Config, _eventHubConfiguration, context);
            }

            return binding != null;
        }

        public override void Initialize()
        {
            // Apply ServiceBus configuration
            ServiceBusConfiguration serviceBusConfig = new ServiceBusConfiguration();
            JObject configSection = (JObject)Metadata.GetValue("serviceBus", StringComparison.OrdinalIgnoreCase);
            JToken value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("maxConcurrentCalls", StringComparison.OrdinalIgnoreCase, out value))
                {
                    serviceBusConfig.MessageOptions.MaxConcurrentCalls = (int)value;
                }

                if (configSection.TryGetValue("prefetchCount", StringComparison.OrdinalIgnoreCase, out value))
                {
                    serviceBusConfig.PrefetchCount = (int)value;
                }
            }

            Config.UseServiceBus(serviceBusConfig);
            Config.UseEventHub(_eventHubConfiguration);
        }

        public override bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            assembly = null;

            if (string.Compare(assemblyName, "Microsoft.ServiceBus", StringComparison.OrdinalIgnoreCase) == 0)
            {
                assembly = typeof(BrokeredMessage).Assembly;
            }

            return assembly != null;
        }

        private class EventHubScriptBinding : ScriptBinding
        {
            private readonly string _storageConnectionString;
            private readonly EventHubConfiguration _eventHubConfiguration;
            private readonly INameResolver _nameResolver;

            public EventHubScriptBinding(JobHostConfiguration hostConfig, EventHubConfiguration eventHubConfig, ScriptBindingContext context) : base(context)
            {
                _eventHubConfiguration = eventHubConfig;
                _storageConnectionString = hostConfig.StorageConnectionString;
                _nameResolver = hostConfig.NameResolver;
            }

            public override Type DefaultType
            {
                get
                {
                    if (Context.Access == FileAccess.Read)
                    {
                        return string.Compare("binary", Context.DataType, StringComparison.OrdinalIgnoreCase) == 0
                            ? typeof(byte[]) : typeof(string);
                    }
                    else
                    {
                        return typeof(IAsyncCollector<byte[]>);
                    }
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                Collection<Attribute> attributes = new Collection<Attribute>();

                string eventHubName = Context.GetMetadataValue<string>("path");
                if (!string.IsNullOrEmpty(eventHubName))
                {
                    eventHubName = _nameResolver.ResolveWholeString(eventHubName);
                }

                string connectionString = Context.GetMetadataValue<string>("connection");
                if (!string.IsNullOrEmpty(connectionString))
                {
                    connectionString = _nameResolver.Resolve(connectionString);
                }

                if (Context.IsTrigger)
                {
                    attributes.Add(new EventHubTriggerAttribute(eventHubName));

                    string eventProcessorHostName = Guid.NewGuid().ToString();
                    string storageConnectionString = _storageConnectionString;

                    string consumerGroup = Context.GetMetadataValue<string>("consumerGroup");
                    if (consumerGroup == null)
                    {
                        consumerGroup = Microsoft.ServiceBus.Messaging.EventHubConsumerGroup.DefaultGroupName;
                    }

                    var eventProcessorHost = new Microsoft.ServiceBus.Messaging.EventProcessorHost(
                         eventProcessorHostName,
                         eventHubName,
                         consumerGroup,
                         connectionString,
                         storageConnectionString);

                    _eventHubConfiguration.AddEventProcessorHost(eventHubName, eventProcessorHost);
                }
                else
                {
                    attributes.Add(new EventHubAttribute(eventHubName));

                    var client = Microsoft.ServiceBus.Messaging.EventHubClient.CreateFromConnectionString(connectionString, eventHubName);
                    _eventHubConfiguration.AddEventHubClient(eventHubName, client);
                }

                return attributes;
            }
        }

        private class ServiceBusScriptBinding : ScriptBinding
        {
            public ServiceBusScriptBinding(ScriptBindingContext context) : base(context)
            {
            }
            public override Type DefaultType
            {
                get
                {
                    if (Context.Access == FileAccess.Read)
                    {
                        return string.Compare("binary", Context.DataType, StringComparison.OrdinalIgnoreCase) == 0
                            ? typeof(byte[]) : typeof(string);
                    }
                    else
                    {
                        return typeof(IAsyncCollector<byte[]>);
                    }
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                Collection<Attribute> attributes = new Collection<Attribute>();

                string queueName = Context.GetMetadataValue<string>("queueName");
                string topicName = Context.GetMetadataValue<string>("topicName");
                string subscriptionName = Context.GetMetadataValue<string>("subscriptionName");
                var accessRights = Context.GetMetadataEnumValue<Microsoft.ServiceBus.Messaging.AccessRights>("accessRights");

                if (Context.IsTrigger)
                {
                    if (!string.IsNullOrEmpty(topicName) && !string.IsNullOrEmpty(subscriptionName))
                    {
                        attributes.Add(new ServiceBusTriggerAttribute(topicName, subscriptionName, accessRights));
                    }
                    else if (!string.IsNullOrEmpty(queueName))
                    {
                        attributes.Add(new ServiceBusTriggerAttribute(queueName, accessRights));
                    }
                }
                else
                {
                    attributes.Add(new ServiceBusAttribute(queueName ?? topicName, accessRights));
                }

                if (attributes.Count == 0)
                {
                    throw new InvalidOperationException("Invalid ServiceBus trigger configuration.");
                }

                string account = Context.GetMetadataValue<string>("connection");
                if (!string.IsNullOrEmpty(account))
                {
                    attributes.Add(new ServiceBusAccountAttribute(account));
                }

                return attributes;
            }
        }
    }
}
