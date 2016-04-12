// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ServiceBusBindingMetadata : BindingMetadata
    {
        public ServiceBusBindingMetadata()
        {
            AccessRights = AccessRights.Manage;
        }

        public string QueueName { get; set; }

        public string TopicName { get; set; }

        public string SubscriptionName { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public AccessRights AccessRights { get; set; }
    }
}
