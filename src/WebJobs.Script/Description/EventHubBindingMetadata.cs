// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.ServiceBus;
using System;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class EventHubBindingMetadata : BindingMetadata
    {
        [AllowNameResolution]
        public string ConnectionString { get; set; }

        [AllowNameResolution]
        public string Path { get; set; }

        public override void ApplyToConfig(JobHostConfigurationBuilder configBuilder)
        {
            EventHubConfiguration eventHubConfig = configBuilder.EventHubConfiguration;

            if (this.IsTrigger)
            {
                string eventProcessorHostName = Guid.NewGuid().ToString();

                string storageConnectionString = configBuilder.Config.StorageConnectionString;

                var eventProcessorHost = new Microsoft.ServiceBus.Messaging.EventProcessorHost(
                     eventProcessorHostName,
                     this.Path,
                     Microsoft.ServiceBus.Messaging.EventHubConsumerGroup.DefaultGroupName,
                     this.ConnectionString,
                     storageConnectionString);

                eventHubConfig.AddEventProcessorHost(this.Path, eventProcessorHost);

            }
            else
            {                
                var client = Microsoft.ServiceBus.Messaging.EventHubClient.CreateFromConnectionString(
                    this.ConnectionString, this.Path);

                eventHubConfig.AddEventHubClient(this.Path, client);
            }
        }        
    }
}
