// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// Provide configuration for event hubs. 
    /// This is primarily mapping names to underlying EventHub listener and receiver objects from the EventHubs SDK. 
    /// </summary>
    public class EventHubConfiguration : IExtensionConfigProvider, IEventHubProvider
    {
        // Event Hub Names are case-insensitive.
        // The same path can have multiple connection strings with different permissions (sending and receiving), 
        // so we track senders and receivers separately and infer which one to use based on the EventHub (sender) vs. EventHubTrigger (receiver) attribute. 
        // Connection strings may also encapsulate different endpoints. 
        private readonly Dictionary<string, EventHubClient> _senders = new Dictionary<string, EventHubClient>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReceiverCreds> _receiverCreds = new Dictionary<string, ReceiverCreds>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EventProcessorHost> _explicitlyProvidedHosts = new Dictionary<string, EventProcessorHost>(StringComparer.OrdinalIgnoreCase);

        private readonly EventProcessorOptions _options;
        private readonly PartitionManagerOptions _partitionOptions; // optional, used to create EventProcessorHost

        private string _defaultStorageString; // set to JobHostConfig.StorageConnectionString

        /// <summary>
        /// default constructor. Callers can reference this without having any assembly references to service bus assemblies. 
        /// </summary>
        public EventHubConfiguration()
            : this(null, null)
        {
        }

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="options">The optional <see cref="EventProcessorOptions"/> to use when receiving events.</param>
        /// <param name="partitionOptions">Optional <see cref="PartitionManagerOptions"/> to use to configure any EventProcessorHosts. </param>
        public EventHubConfiguration(
            EventProcessorOptions options, 
            PartitionManagerOptions partitionOptions = null)
        {
            if (options == null)
            {
                options = EventProcessorOptions.DefaultOptions;
                options.MaxBatchSize = 1000;
            }
            _partitionOptions = partitionOptions;

            _options = options;
        }

        /// <summary>
        /// Add an existing client for sending messages to an event hub.  Infer the eventHub name from client.path
        /// </summary>
        /// <param name="client"></param>
        public void AddEventHubClient(EventHubClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }
            string eventHubName = client.Path;
            AddEventHubClient(eventHubName, client);
        }

        /// <summary>
        /// Add an existing client for sending messages to an event hub.  Infer the eventHub name from client.path
        /// </summary>
        /// <param name="eventHubName">name of the event hub</param>
        /// <param name="client"></param>
        public void AddEventHubClient(string eventHubName, EventHubClient client)
        {
            if (eventHubName == null)
            {
                throw new ArgumentNullException("eventHubName");
            }
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }
           
            _senders[eventHubName] = client;
        }

        /// <summary>
        /// Add a connection for sending messages to an event hub. Connect via the connection string. 
        /// </summary>
        /// <param name="eventHubName">name of the event hub. </param>
        /// <param name="sendConnectionString">connection string for sending messages. If this includes an EntityPath, it takes precedence over the eventHubName parameter.</param>
        public void AddSender(string eventHubName, string sendConnectionString)
        {
            if (eventHubName == null)
            {
                throw new ArgumentNullException("eventHubName");
            }
            if (sendConnectionString == null)
            {
                throw new ArgumentNullException("sendConnectionString");
            }

            ServiceBusConnectionStringBuilder sb = new ServiceBusConnectionStringBuilder(sendConnectionString);
            if (string.IsNullOrWhiteSpace(sb.EntityPath))
            {
                sb.EntityPath = eventHubName;
            }            

            var client = EventHubClient.CreateFromConnectionString(sb.ToString());
            AddEventHubClient(eventHubName, client);
        }

        /// <summary>
        /// Add a connection for listening on events from an event hub. 
        /// </summary>
        /// <param name="eventHubName">Name of the event hub</param>
        /// <param name="listener">initialized listener object</param>
        /// <remarks>The EventProcessorHost type is from the ServiceBus SDK. 
        /// Allow callers to bind to EventHubConfiguration without needing to have a direct assembly reference to the ServiceBus SDK. 
        /// The compiler needs to resolve all types in all overloads, so give methods that use the ServiceBus SDK types unique non-overloaded names
        /// to avoid eager compiler resolution. 
        /// </remarks>
        public void AddEventProcessorHost(string eventHubName, EventProcessorHost listener)
        {
            if (eventHubName == null)
            {
                throw new ArgumentNullException("eventHubName");
            }
            if (listener == null)
            {
                throw new ArgumentNullException("listener");
            }

            _explicitlyProvidedHosts[eventHubName] = listener;
        }

        /// <summary>
        /// Add a connection for listening on events from an event hub. Connect via the connection string and use the SDK's built-in storage account.
        /// </summary>
        /// <param name="eventHubName">name of the event hub</param>
        /// <param name="receiverConnectionString">connection string for receiving messages. This can encapsulate other service bus properties like the namespace and endpoints.</param>
        public void AddReceiver(string eventHubName, string receiverConnectionString)
        {
            if (eventHubName == null)
            {
                throw new ArgumentNullException("eventHubName");
            }
            if (receiverConnectionString == null)
            {
                throw new ArgumentNullException("receiverConnectionString");
            }

            this._receiverCreds[eventHubName] = new ReceiverCreds
            {
                 EventHubConnectionString = receiverConnectionString
            };
        }

        /// <summary>
        /// Add a connection for listening on events from an event hub. Connect via the connection string and use the supplied storage account
        /// </summary>
        /// <param name="eventHubName">name of the event hub</param>
        /// <param name="receiverConnectionString">connection string for receiving messages</param>
        /// <param name="storageConnectionString">storage connection string that the EventProcessorHost client will use to coordinate multiple listener instances. </param>
        public void AddReceiver(string eventHubName, string receiverConnectionString, string storageConnectionString)
        {
            if (eventHubName == null)
            {
                throw new ArgumentNullException("eventHubName");
            }
            if (receiverConnectionString == null)
            {
                throw new ArgumentNullException("receiverConnectionString");
            }
            if (storageConnectionString == null)
            {
                throw new ArgumentNullException("storageConnectionString");
            }

            this._receiverCreds[eventHubName] = new ReceiverCreds
            {
                EventHubConnectionString = receiverConnectionString,
                StorageConnectionString = storageConnectionString
            };
        }
        
        internal EventHubClient GetEventHubClient(string eventHubName)
        {
            EventHubClient client;
            if (_senders.TryGetValue(eventHubName, out client))             
            {
                return client;
            }
            throw new InvalidOperationException("No event hub sender named " + eventHubName);
        }

        EventProcessorHost IEventHubProvider.GetEventProcessorHost(string eventHubName, string consumerGroup)
        {
            ReceiverCreds creds;
            if (this._receiverCreds.TryGetValue(eventHubName, out creds))
            {
                // Common case. Create a new EventProcessorHost instance to listen. 
                string eventProcessorHostName = Guid.NewGuid().ToString();

                if (consumerGroup == null)
                {
                    consumerGroup = EventHubConsumerGroup.DefaultGroupName;
                }
                var storageConnectionString = creds.StorageConnectionString;
                if (storageConnectionString == null)
                {
                    storageConnectionString = _defaultStorageString;
                }

                // If the connection string provides a hub name, that takes precedence. 
                // Note that connection strings *can't* specify a consumerGroup, so must always be passed in. 
                string actualPath = eventHubName;
                ServiceBusConnectionStringBuilder sb = new ServiceBusConnectionStringBuilder(creds.EventHubConnectionString);
                if (sb.EntityPath != null)
                {
                    actualPath = sb.EntityPath;
                    sb.EntityPath = null; // need to remove to use with EventProcessorHost
                }

                EventProcessorHost host = new EventProcessorHost(
                   eventProcessorHostName,
                   actualPath,
                   consumerGroup,
                   sb.ToString(),
                   storageConnectionString);

                if (_partitionOptions != null)
                {
                    host.PartitionManagerOptions = _partitionOptions;
                }

                return host;
            }
            else
            {
                // Rare case: a power-user caller specifically provided an event processor host to use. 
                EventProcessorHost host;
                if (_explicitlyProvidedHosts.TryGetValue(eventHubName, out host))
                {
                    return host;
                }
            }
            throw new InvalidOperationException("No event hub receiver named " + eventHubName);
        }

        EventProcessorOptions IEventHubProvider.GetOptions()
        {
            return _options;
        }

        void IExtensionConfigProvider.Initialize(ExtensionConfigContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            _defaultStorageString = context.Config.StorageConnectionString;

            // get the services we need to construct our binding providers
            INameResolver nameResolver = context.Config.NameResolver;
            IExtensionRegistry extensions = context.Config.GetService<IExtensionRegistry>();

            IConverterManager cm = context.Config.GetService<IConverterManager>();
            cm.AddConverter<string, EventData>(ConvertString2EventData);
            cm.AddConverter<EventData, string>(ConvertEventData2String);
            cm.AddConverter<byte[], EventData>(ConvertBytes2EventData); // direct, handles non-string representations

            var bf = new BindingFactory(nameResolver, cm);

            // register our trigger binding provider
            var triggerBindingProvider = new EventHubTriggerAttributeBindingProvider(nameResolver, cm, this);
            extensions.RegisterExtension<ITriggerBindingProvider>(triggerBindingProvider);

            // register our binding provider
            var ruleOutput = bf.BindToAsyncCollector<EventHubAttribute, EventData>(BuildFromAttribute);
            extensions.RegisterBindingRules<EventHubAttribute>(ruleOutput);
        }

        private IAsyncCollector<EventData> BuildFromAttribute(EventHubAttribute attribute)
        {
            EventHubClient client = this.GetEventHubClient(attribute.EventHubName);
            return new EventHubAsyncCollector(client);
        }

        // EventData --> String
        private static string ConvertEventData2String(EventData x)
        {
            return Encoding.UTF8.GetString(x.GetBytes());
        }

        private static EventData ConvertBytes2EventData(byte[] input)
        {
            var eventData = new EventData(input);
            return eventData;
        }

        private static EventData ConvertString2EventData(string input)
        {
            var eventData = new EventData(Encoding.UTF8.GetBytes(input));
            return eventData;
        }

        // Hold credentials for a given eventHub name. 
        // Multiple consumer groups (and multiple listeners) on the same hub can share the same credentials. 
        private class ReceiverCreds
        {
            // Required.  
            public string EventHubConnectionString { get; set; }

            // Optional. If not found, use the stroage from JobHostConfiguration
            public string StorageConnectionString { get; set; }
        }
    }
}
