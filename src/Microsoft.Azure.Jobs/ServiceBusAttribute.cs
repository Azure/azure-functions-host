using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Binds to Azure Service Bus Queues and Topics.</summary>
    /// <remarks>
    /// The method parameter type can be one of the following:
    /// <list type="bullet">
    /// <item><description>BrokeredMessage (out param)</description></item>
    /// <item><description><see cref="string"/> (out param)</description></item>
    /// <item><description><see cref="T:byte[]"/> (out param)</description></item>
    /// <item><description>A user-defined type (out param, serialized as JSON)</description></item>
    /// <item><description>
    /// <see cref="ICollection{T}"/> of these types (to enqueue multiple messages via <see cref="ICollection{T}.Add"/>
    /// </description></item>
    /// </list>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Parameter)]
    [DebuggerDisplay("{QueueOrTopicName,nq}")]
    public sealed class ServiceBusAttribute : Attribute
    {
        private readonly string _queueOrTopicName;

        /// <summary>Initializes a new instance of the <see cref="ServiceBusAttribute"/> class.</summary>
        /// <param name="queueOrTopicName">The name of the queue or topic to which to bind.</param>
        public ServiceBusAttribute(string queueOrTopicName)
        {
            _queueOrTopicName = queueOrTopicName;
        }

        /// <summary>Gets the name of the queue or topic to which to bind.</summary>
        public string QueueOrTopicName
        {
            get { return _queueOrTopicName; }
        }
    }
}
