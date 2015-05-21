// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Globalization;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class ServiceBusTriggerParameterDescriptor : TriggerParameterDescriptor
    {
        /// <summary>Gets or sets the name of the Service Bus namespace.</summary>
        public string NamespaceName { get; set; }

        /// <summary>Gets or sets the name of the queue.</summary>
        /// <remarks>When binding to a subscription in a topic, returns <see langword="null"/>.</remarks>
        public string QueueName { get; set; }

        /// <summary>Gets or sets the name of the queue.</summary>
        /// <remarks>When binding to a queue, returns <see langword="null"/>.</remarks>
        public string TopicName { get; set; }

        /// <summary>Gets or sets the name of the subscription in <see cref="TopicName"/>.</summary>
        /// <remarks>When binding to a queue, returns <see langword="null"/>.</remarks>
        public string SubscriptionName { get; set; }

        /// <inheritdoc />
        public override string GetTriggerReason(IDictionary<string, string> arguments)
        {
            string path;
            if (QueueName != null)
            {
                path = QueueName;
            }
            else
            {
                path = string.Format(CultureInfo.InvariantCulture, "{0}/Subscriptions/{1}", TopicName, SubscriptionName);
            }

            return string.Format(CultureInfo.CurrentCulture, "New ServiceBus message detected on '{0}.", path);
        }
    }
}
