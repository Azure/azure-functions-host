// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Represents an attribute that binds a parameter to a Service Bus Queue message, causing the method to run when a
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
    public sealed class ServiceBusTriggerAttribute : Attribute
    {
        private readonly string _queueName;
        private readonly string _topicName;
        private readonly string _subscriptionName;

        /// <summary>Initializes a new instance of the <see cref="ServiceBusTriggerAttribute"/> class.</summary>
        /// <param name="queueName">The name of the queue to which to bind.</param>
        public ServiceBusTriggerAttribute(string queueName)
        {
            _queueName = queueName;
        }

        /// <summary>Initializes a new instance of the <see cref="ServiceBusTriggerAttribute"/> class.</summary>
        /// <param name="topicName">The name of the topic to which to bind.</param>
        /// <param name="subscriptionName">The name of the subscription in <paramref name="topicName"/> to which to bind.</param>
        public ServiceBusTriggerAttribute(string topicName, string subscriptionName)
        {
            _topicName = topicName;
            _subscriptionName = subscriptionName;
        }

        /// <summary>Gets the name of the queue to which to bind.</summary>
        /// <remarks>When binding to a subscription in a topic, returns <see langword="null"/>.</remarks>
        public string QueueName
        {
            get { return _queueName; }
        }

        /// <summary>Gets the name of the topic to which to bind.</summary>
        /// <remarks>When binding to a queue, returns <see langword="null"/>.</remarks>
        public string TopicName
        {
            get { return _topicName; }
        }

        /// <summary>Gets the name of the subscription in <see cref="TopicName"/> to which to bind.</summary>
        /// <remarks>When binding to a queue, returns <see langword="null"/>.</remarks>
        public string SubscriptionName
        {
            get { return _subscriptionName; }
        }

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
