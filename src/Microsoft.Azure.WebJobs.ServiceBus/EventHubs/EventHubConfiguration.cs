// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.EventHubs.Processor;

namespace Microsoft.Azure.WebJobs.ServiceBus
{
    /// <summary>
    /// Provide configuration for event hubs. 
    /// This is primarily mapping names to underlying EventHub listener and receiver objects from the EventHubs SDK. 
    /// </summary>
    public class EventHubConfiguration : IExtensionConfigProvider
    {
        // Event Hub Names are case-insensitive.
        // The same path can have multiple connection strings with different permissions (sending and receiving), 
        // so we track senders and receivers separately and infer which one to use based on the EventHub (sender) vs. EventHubTrigger (receiver) attribute. 
        // Connection strings may also encapsulate different endpoints. 
        private readonly Dictionary<string, EventHubClient> _senders = new Dictionary<string, EventHubClient>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReceiverCreds> _receiverCreds = new Dictionary<string, ReceiverCreds>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EventProcessorHost> _explicitlyProvidedHosts = new Dictionary<string, EventProcessorHost>(StringComparer.OrdinalIgnoreCase);

        private readonly EventProcessorOptions _options;

        private string _defaultStorageString; // set to JobHostConfig.StorageConnectionString
        private int _batchCheckpointFrequency = 1;

        /// <summary>
        /// Name of the blob container that the EventHostProcessor instances uses to coordinate load balancing listening on an event hub. 
        /// Each event hub gets its own blob prefix within the container. 
        /// </summary>
        public const string LeaseContainerName = "azure-webjobs-eventhub";

        /// <summary>
        /// default constructor. Callers can reference this without having any assembly references to service bus assemblies. 
        /// </summary>
        public EventHubConfiguration()
            : this(null)
        {
        }

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="options">The optional <see cref="EventProcessorOptions"/> to use when receiving events.</param>
        /// <param name="partitionOptions">Optional <see cref="PartitionManagerOptions"/> to use to configure any EventProcessorHosts. </param>
        public EventHubConfiguration(
            EventProcessorOptions options)
        {
            if (options == null)
            {
                options = EventProcessorOptions.DefaultOptions;
                options.MaxBatchSize = 64;
                options.PrefetchCount = options.MaxBatchSize * 4;
            }

            _options = options;
        }

        /// <summary>
        /// Gets or sets the number of batches to process before creating an EventHub cursor checkpoint. Default 1.
        /// </summary>
        public int BatchCheckpointFrequency
        {
            get
            {
                return _batchCheckpointFrequency;
            }

            set
            {
                if (value <= 0)
                {
                    throw new InvalidOperationException("Batch checkpoint frequency must be larger than 0.");
                }
                _batchCheckpointFrequency = value;
            }
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
            string eventHubName = client.EventHubName;
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
        
        internal EventHubClient GetEventHubClient(string eventHubName, string connection)
        {
            EventHubClient client;
            if (_senders.TryGetValue(eventHubName, out client))             
            {
                return client;
            }
            else if (!string.IsNullOrWhiteSpace(connection))
            {
                AddSender(eventHubName, connection);
                return _senders[eventHubName];
            }
            throw new InvalidOperationException("No event hub sender named " + eventHubName);
        }

        // Lookup a listener for receiving events given the name provided in the [EventHubTrigger] attribute. 
        internal EventProcessorHost GetEventProcessorHost(string eventHubName, string consumerGroup)
        {
            ReceiverCreds creds;
            if (this._receiverCreds.TryGetValue(eventHubName, out creds))
            {
                // Common case. Create a new EventProcessorHost instance to listen. 
                string eventProcessorHostName = Guid.NewGuid().ToString();

                if (consumerGroup == null)
                {
                    consumerGroup = PartitionReceiver.DefaultConsumerGroupName;
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

                var @namespace = GetServiceBusNamespace(sb);
                var blobPrefix = GetBlobPrefix(actualPath, @namespace);

                // Use blob prefix support available in EPH starting in 2.2.6 
                EventProcessorHost host = new EventProcessorHost(
                    hostName: eventProcessorHostName,
                    eventHubPath: actualPath,
                    consumerGroupName: consumerGroup,
                    eventHubConnectionString: sb.ToString(),
                    storageConnectionString: storageConnectionString,
                    leaseContainerName: LeaseContainerName,
                    storageBlobPrefix: blobPrefix);
                
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

        private static string EscapeStorageCharacter(char character)
        {
            var ordinalValue = (ushort)character;
            if (ordinalValue < 0x100)
            {
                return string.Format(CultureInfo.InvariantCulture, ":{0:X2}", ordinalValue);
            }
            else
            {
                return string.Format(CultureInfo.InvariantCulture, "::{0:X4}", ordinalValue);
            }
        }
                
        // Escape a blob path.  
        // For diagnostics, we want human-readble strings that resemble the input. 
        // Inputs are most commonly alphanumeric with a fex extra chars (dash, underscore, dot). 
        // Escape character is a ':', which is also escaped. 
        // Blob names are case sensitive; whereas input is case insensitive, so normalize to lower.  
        private static string EscapeBlobPath(string path)
        {
            StringBuilder sb = new StringBuilder(path.Length);
            foreach (char c in path)
            {
                if (c >= 'a' && c <= 'z')
                {
                    sb.Append(c);
                }
                else if (c == '-' || c == '_' || c == '.') 
                {
                    // Potentially common carahcters. 
                    sb.Append(c);
                }
                else if (c >= 'A' && c <= 'Z')
                {
                    sb.Append((char)(c - 'A' + 'a')); // ToLower
                }
                else if (c >= '0' && c <= '9')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(EscapeStorageCharacter(c));
                }
            }

            return sb.ToString();
        }

        private static string GetServiceBusNamespace(ServiceBusConnectionStringBuilder connectionString)
        {
            // EventHubs only have 1 endpoint. 
            var url = connectionString.Endpoint;
            var @namespace = url.Host;
            return @namespace;
        }

        /// <summary>
        /// Get the blob prefix used with EventProcessorHost for a given event hub.  
        /// </summary>
        /// <param name="eventHubName">the event hub path</param>
        /// <param name="serviceBusNamespace">the event hub's service bus namespace.</param>
        /// <returns>a blob prefix path that can be passed to EventProcessorHost.</returns>
        /// <remarks>
        /// An event hub is defined by it's path and namespace. The namespace is extracted from the connection string. 
        /// This must be an injective one-to-one function because:
        /// 1. multiple machines listening on the same event hub must use the same blob prefix. This means it must be deterministic. 
        /// 2. different event hubs must not resolve to the same path. 
        /// </remarks>        
        public static string GetBlobPrefix(string eventHubName, string serviceBusNamespace)
        {
            if (eventHubName == null)
            {
                throw new ArgumentNullException("eventHubName");
            }
            if (serviceBusNamespace == null)
            {
                throw new ArgumentNullException("serviceBusNamespace");
            }

            string key = EscapeBlobPath(serviceBusNamespace) + "/" + EscapeBlobPath(eventHubName) + "/";
            return key;
        }

        // Get the eventhub options, used by the EventHub SDK for listening on event. 
        internal EventProcessorOptions GetOptions() => _options;

        void IExtensionConfigProvider.Initialize(ExtensionConfigContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            // apply at eventProcessorOptions level (maxBatchSize, prefetchCount)
            context.ApplyConfig(_options, "eventHub");

            // apply at config level (batchCheckpointFrequency)
            context.ApplyConfig(this, "eventHub");

            _defaultStorageString = context.Config.StorageConnectionString;

            context
                .AddConverter<string, EventData>(ConvertString2EventData)
                .AddConverter<EventData, string>(ConvertEventData2String)
                .AddConverter<byte[], EventData>(ConvertBytes2EventData)
                .AddConverter<EventData, byte[]>(ConvertEventData2Bytes);

            // register our trigger binding provider
            INameResolver nameResolver = context.Config.NameResolver;
            IConverterManager cm = context.Config.GetService<IConverterManager>();
            var triggerBindingProvider = new EventHubTriggerAttributeBindingProvider(nameResolver, cm, this);
            context.AddBindingRule<EventHubTriggerAttribute>()
                .BindToTrigger(triggerBindingProvider);

            // register our binding provider
            context.AddBindingRule<EventHubAttribute>()
                .BindToCollector(BuildFromAttribute);           
        }

        private IAsyncCollector<EventData> BuildFromAttribute(EventHubAttribute attribute)
        {
            EventHubClient client = this.GetEventHubClient(attribute.EventHubName, attribute.Connection);
            return new EventHubAsyncCollector(client);
        }

        private static string ConvertEventData2String(EventData x) 
            => Encoding.UTF8.GetString(ConvertEventData2Bytes(x));

        private static EventData ConvertBytes2EventData(byte[] input) 
            => new EventData(input);

        private static byte[] ConvertEventData2Bytes(EventData input)
            => input.Body.Array;

        private static EventData ConvertString2EventData(string input) 
            => ConvertBytes2EventData(Encoding.UTF8.GetBytes(input));

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
