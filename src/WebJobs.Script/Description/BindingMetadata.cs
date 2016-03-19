// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class BindingMetadata
    {
        public string Name { get; set; }

        public string StorageAccount { get; set; }

        public BindingType Type { get; set; }

        public BindingDirection Direction { get; set; }

        public bool IsTrigger
        {
            get
            {
                return
                    Type == BindingType.TimerTrigger ||
                    Type == BindingType.BlobTrigger ||
                    Type == BindingType.HttpTrigger ||
                    Type == BindingType.QueueTrigger ||
                    Type == BindingType.EventHubTrigger ||
                    Type == BindingType.ServiceBusTrigger ||
                    Type == BindingType.ManualTrigger;
            }
        }

        // Bindings can include information that drives the JobHostConfiguration. 
        public virtual void ApplyToConfig(JobHostConfigurationBuilder configBuilder)
        {
            // default is nop
        }        
    }
}
