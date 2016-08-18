// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// Provide configuration for event hubs. 
    /// This is primarily mapping names to underlying EventHub listener and receiver objects from the EventHubs SDK. 
    /// </summary>
    public class EventHubConfiguration : IExtensionConfigProvider, IEventHubProvider
    {
        // Event Hub Names are case-insensitive
        private readonly Dictionary<string, EventHubClient> _senders = new Dictionary<string, EventHubClient>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EventProcessorHost> _listeners  = new Dictionary<string, EventProcessorHost>(StringComparer.OrdinalIgnoreCase);
        private readonly EventProcessorOptions _options;
        private readonly List<Action<JobHostConfiguration>> _deferredWork = new List<Action<JobHostConfiguration>>();

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="options">The optional <see cref="EventProcessorOptions"/> to use when receiving events.</param>
        public EventHubConfiguration(EventProcessorOptions options = null)
        {
            if (options == null)
            {
                options = EventProcessorOptions.DefaultOptions;
                options.MaxBatchSize = 1000;
            }

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
        /// <param name="sendConnectionString">connection string for sending messages</param>
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
            var client = EventHubClient.CreateFromConnectionString(sendConnectionString, eventHubName);
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

            _listeners[eventHubName] = listener;
        }

        /// <summary>
        /// Add a connection for listening on events from an event hub. Connect via the connection string and use the SDK's built-in storage account.
        /// </summary>
        /// <param name="eventHubName">name of the event hub</param>
        /// <param name="receiverConnectionString">connection string for receiving messages</param>
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

            // We can't get the storage string until we get a JobHostConfig. 
            // So verify the parameter upfront, but defer the creation. 
            _deferredWork.Add((jobHostConfig) =>
           {
               string storageConnectionString = jobHostConfig.StorageConnectionString;
               this.AddReceiver(eventHubName, receiverConnectionString, storageConnectionString);
           });
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

            string eventProcessorHostName = Guid.NewGuid().ToString();

            EventProcessorHost eventProcessorHost = new EventProcessorHost(
                eventProcessorHostName, 
                eventHubName, 
                EventHubConsumerGroup.DefaultGroupName,
                receiverConnectionString, 
                storageConnectionString);

            this.AddEventProcessorHost(eventHubName, eventProcessorHost);
        }
        
        private EventHubClient GetEventHubClient(string eventHubName)
        {
            EventHubClient client;
            if (_senders.TryGetValue(eventHubName, out client))             
            {
                return client;
            }
            throw new InvalidOperationException("No event hub sender named " + eventHubName);
        }

        EventProcessorHost IEventHubProvider.GetEventProcessorHost(string eventHubName)
        {
            EventProcessorHost host;
            if (_listeners.TryGetValue(eventHubName, out host))
            {
                return host;
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

            // Deferred list 
            foreach (var action in _deferredWork)
            {
                action(context.Config);
            }
            _deferredWork.Clear();

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
    }
}
