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
        private readonly string _serviceBusAssemblyName;
        private EventHubConfiguration _eventHubConfiguration;

        public ServiceBusScriptBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter)
            : base(config, hostMetadata, traceWriter)
        {
            _serviceBusAssemblyName = typeof(BrokeredMessage).Assembly.GetName().Name;
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

            EventProcessorOptions eventProcessorOptions = EventProcessorOptions.DefaultOptions;
            eventProcessorOptions.MaxBatchSize = 1000;
            configSection = (JObject)Metadata.GetValue("eventHub", StringComparison.OrdinalIgnoreCase);
            if (configSection != null)
            {
                if (configSection.TryGetValue("maxBatchSize", StringComparison.OrdinalIgnoreCase, out value))
                {
                    eventProcessorOptions.MaxBatchSize = (int)value;
                }

                if (configSection.TryGetValue("prefetchCount", StringComparison.OrdinalIgnoreCase, out value))
                {
                    eventProcessorOptions.PrefetchCount = (int)value;
                }
            }
            _eventHubConfiguration = new EventHubConfiguration(eventProcessorOptions);

            Config.UseServiceBus(serviceBusConfig);
            Config.UseEventHub(_eventHubConfiguration);
        }

        public override bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            assembly = null;

            if (string.Compare(assemblyName, _serviceBusAssemblyName, StringComparison.OrdinalIgnoreCase) == 0)
            {
                assembly = typeof(BrokeredMessage).Assembly;
            }

            return assembly != null;
        }

        private class EventHubScriptBinding : ScriptBinding
        {
            private readonly EventHubConfiguration _eventHubConfiguration;
            private readonly INameResolver _nameResolver;

            public EventHubScriptBinding(JobHostConfiguration hostConfig, EventHubConfiguration eventHubConfig, ScriptBindingContext context) : base(context)
            {
                _eventHubConfiguration = eventHubConfig;
                _nameResolver = hostConfig.NameResolver;
            }

            public override Type DefaultType
            {
                get
                {
                    if (Context.Access == FileAccess.Read)
                    {
                        Type type = string.Compare("binary", Context.DataType, StringComparison.OrdinalIgnoreCase) == 0
                            ? typeof(byte[]) : typeof(string);

                        //if (string.Compare("many", Context.Cardinality, StringComparison.OrdinalIgnoreCase) == 0)
                        //{
                        //    // arrays are supported for both trigger input as well
                        //    // as output bindings
                        //    type = type.MakeArrayType();
                        //}

                        return type;
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
                    var attribute = new EventHubTriggerAttribute(eventHubName);
                    string consumerGroup = Context.GetMetadataValue<string>("consumerGroup");
                    if (consumerGroup != null)
                    {
                        consumerGroup = _nameResolver.ResolveWholeString(consumerGroup);
                        attribute.ConsumerGroup = consumerGroup;
                    }
                    attributes.Add(attribute);
                    _eventHubConfiguration.AddReceiver(eventHubName, connectionString);
                }
                else
                {
                    attributes.Add(new EventHubAttribute(eventHubName));

                    _eventHubConfiguration.AddSender(eventHubName, connectionString);
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
