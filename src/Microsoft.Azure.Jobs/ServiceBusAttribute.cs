using System;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Binds to Azure Service Bus entities (Queues, Topics and Subscriptions).
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class ServiceBusAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the ServiceBusAttribute class, defaulting <see cref="EntityName" /> to the decorated parameter's name.
        /// </summary>
        public ServiceBusAttribute()
        {
        }

        /// <summary>
        /// Initializes a new instance of the ServiceBusAttribute class.
        /// </summary>
        /// <param name="entityName">The name of the entity to bind to. If empty, the 
        /// name of the method parameter is used as the queue name. 
        /// <remarks>For input, it would bind to either a Queue. 
        /// For output, it would either bind to a Queue or a Topic by that name.
        /// </remarks>
        /// </param>
        public ServiceBusAttribute(string entityName)
        {
            EntityName = entityName;
        }

        /// <summary>
        /// Initializes a new instance of the ServiceBusAttribute class.
        /// <remarks>
        /// This overload should only be used for input parameter (without the 'out' keyword) since writing to a subscription directly
        /// is not permitted in Azure ServiceBus.
        /// </remarks>
        /// </summary>
        /// <param name="topic">The Service Bus topic to bind the input parameter to.</param>
        /// <param name="subscription">The Service Bus Subscription in <paramref name="topic"/> to bind the input parameter to.</param>
        public ServiceBusAttribute(string topic, string subscription)
        {
            Topic = topic;
            Subscription = subscription;
        }

        /// <summary>
        /// Gets the name of the entity to bind to. If empty, the name of the method parameter is used
        /// as the entity name.
        /// </summary>
        public string EntityName { get; private set; }

        /// <summary>
        /// Gets the topic name to bind to.
        /// </summary>
        public string Topic { get; private set; }

        /// <summary>
        /// Gets the subscription name in <see cref="Topic"/> to bind to.
        /// </summary>
        public string Subscription { get; private set; }
    }
}