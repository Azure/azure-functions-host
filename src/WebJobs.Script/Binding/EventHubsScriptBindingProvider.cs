// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs.Extensions.EventHubs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    internal class EventHubsScriptBindingProvider : ScriptBindingProvider
    {
        private EventHubConfiguration _eventHubConfiguration;

        public EventHubsScriptBindingProvider(JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter)
            : base(config, hostMetadata, traceWriter)
        {
        }

        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            binding = null;

            if (string.Compare(context.Type, "eventHubTrigger", StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(context.Type, "eventHub", StringComparison.OrdinalIgnoreCase) == 0)
            {
                binding = new EventHubScriptBinding(Config, _eventHubConfiguration, context);
            }

            return binding != null;
        }

        public override void Initialize()
        {
            _eventHubConfiguration = new EventHubConfiguration();
            Config.AddExtension(_eventHubConfiguration);
        }

        // TODO: ensure same references to SB assemblies if possible
        public override bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            assembly = null;

            return Utility.TryMatchAssembly(assemblyName, typeof(EventData), out assembly) ||
                   Utility.TryMatchAssembly(assemblyName, typeof(EventHubAttribute), out assembly);
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

                        if (string.Compare("many", Context.Cardinality, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            // arrays are supported for both trigger input as well
                            // as output bindings
                            type = type.MakeArrayType();
                        }

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
    }
}
