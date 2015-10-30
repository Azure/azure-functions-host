// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Attribute used to bind a parameter to Azure ServiceBus Queues and Topics.
    /// </summary>
    /// <remarks>
    /// The method parameter type can be one of the following:
    /// <list type="bullet">
    /// <item><description>BrokeredMessage (out parameter)</description></item>
    /// <item><description><see cref="string"/> (out parameter)</description></item>
    /// <item><description><see cref="T:byte[]"/> (out parameter)</description></item>
    /// <item><description>A user-defined type (out parameter, serialized as JSON)</description></item>
    /// <item><description>
    /// <see cref="ICollection{T}"/> of these types (to enqueue multiple messages via <see cref="ICollection{T}.Add"/>
    /// </description></item>
    /// </list>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Parameter)]
    [DebuggerDisplay("{QueueOrTopicName,nq}")]
    public sealed class ServiceBusAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceBusAttribute"/> class.
        /// </summary>
        /// <param name="queueOrTopicName">The name of the queue or topic to bind to.</param>
        public ServiceBusAttribute(string queueOrTopicName)
        {
            QueueOrTopicName = queueOrTopicName;
            Access = AccessRights.Manage;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceBusAttribute"/> class.
        /// </summary>
        /// <param name="queueOrTopicName">The name of the queue or topic to bind to.</param>
        /// <param name="access">The <see cref="AccessRights"/> the client has to the queue or topic.</param>
        public ServiceBusAttribute(string queueOrTopicName, AccessRights access)
        {
            QueueOrTopicName = queueOrTopicName;
            Access = access;
        }

        /// <summary>
        /// Gets the name of the queue or topic to bind to.
        /// </summary>
        public string QueueOrTopicName { get; private set; }

        /// <summary>
        /// Gets the <see cref="AccessRights"/> the client has to the queue or topic.
        /// The default is "Manage".
        /// </summary>
        public AccessRights Access { get; private set; }
    }
}
