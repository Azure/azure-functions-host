// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.ServiceBus;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // Helper to collect configuration updates from the BindingMetadata and ultimately apply to the JobHostConfiguration. 
    public class JobHostConfigurationBuilder
    {
        public JobHostConfigurationBuilder(JobHostConfiguration config)
        {
            this.Config = config;
            this.EventHubConfiguration = new EventHubConfiguration();
        }

        internal JobHostConfiguration Config { get; private set; }

        internal EventHubConfiguration EventHubConfiguration { get; private set; }

        public void Done()
        {
            this.Config.UseEventHub(this.EventHubConfiguration);
        }
    }
}