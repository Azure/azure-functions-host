// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Attribute used to bind a parameter to a ServiceBus Queue message, causing the function to run when a
    /// message is enqueued.
    /// </summary>
    /// <remarks>
    /// The method parameter type can be one of the following:
    /// <list type="bullet">
    /// <item><description>BrokeredMessage</description></item>
    /// <item><description><see cref="string"/></description></item>
    /// <item><description><see cref="T:byte[]"/></description></item>
    /// <item><description>A user-defined type (serialized as JSON)</description></item>
    /// </list>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Parameter)]
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    [ConnectionProvider(typeof(ServiceBusAccountAttribute))]
    public sealed class ServiceBusTriggerAttribute : Attribute, IConnectionProvider
    {
        private readonly string _queueName;
        private readonly string _topicName;
        private readonly string _subscriptionName;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceBusTriggerAttribute"/> class.
        /// </summary>
        /// <param name="queueName">The name of the queue to which to bind.</param>
        public ServiceBusTriggerAttribute(string queueName)
        {
            _queueName = queueName;
            Access = AccessRights.Manage;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceBusTriggerAttribute"/> class.
        /// </summary>
        /// <param name="queueName">The name of the queue to which to bind.</param>
        /// <param name="access">The <see cref="AccessRights"/> the client has to the queue.</param>
        public ServiceBusTriggerAttribute(string queueName, AccessRights access)
        {
            _queueName = queueName;
            Access = access;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceBusTriggerAttribute"/> class.
        /// </summary>
        /// <param name="topicName">The name of the topic to bind to.</param>
        /// <param name="subscriptionName">The name of the subscription in <paramref name="topicName"/> to bind to.</param>
        public ServiceBusTriggerAttribute(string topicName, string subscriptionName)
        {
            _topicName = topicName;
            _subscriptionName = subscriptionName;
            Access = AccessRights.Manage;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceBusTriggerAttribute"/> class.
        /// </summary>
        /// <param name="topicName">The name of the topic to bind to.</param>
        /// <param name="subscriptionName">The name of the subscription in <paramref name="topicName"/> to bind to.</param>
        /// <param name="access">The <see cref="AccessRights"/> the client has to the subscription in the topic.</param>
        public ServiceBusTriggerAttribute(string topicName, string subscriptionName, AccessRights access)
        {
            _topicName = topicName;
            _subscriptionName = subscriptionName;
            Access = access;
        }

        /// <inheritdoc />
        public string Connection { get; set; }

        /// <summary>
        /// Gets the name of the queue to which to bind.
        /// </summary>
        /// <remarks>When binding to a subscription in a topic, returns <see langword="null"/>.</remarks>
        public string QueueName
        {
            get { return _queueName; }
        }

        /// <summary>
        /// Gets the name of the topic to which to bind.
        /// </summary>
        /// <remarks>When binding to a queue, returns <see langword="null"/>.</remarks>
        public string TopicName
        {
            get { return _topicName; }
        }

        /// <summary>
        /// Gets the name of the subscription in <see cref="TopicName"/> to bind to.
        /// </summary>
        /// <remarks>When binding to a queue, returns <see langword="null"/>.</remarks>
        public string SubscriptionName
        {
            get { return _subscriptionName; }
        }

        /// <summary>
        /// Gets the <see cref="AccessRights"/> the client has to the queue or topic subscription.
        /// </summary>
        public AccessRights Access { get; private set; }

        private string DebuggerDisplay
        {
            get
            {
                if (_queueName != null)
                {
                    return _queueName;
                }
                else
                {
                    return _topicName + "/" + _subscriptionName;
                }
            }
        }
    }
}
