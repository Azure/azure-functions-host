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
