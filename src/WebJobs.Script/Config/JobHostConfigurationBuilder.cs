// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.ServiceBus;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // Helper to collect configuration updates from the BindingMetadata and ultimately apply to the JobHostConfiguration. 
    public class JobHostConfigurationBuilder
    {
        public JobHostConfigurationBuilder(JobHostConfiguration config, TraceWriter traceWriter)
        {
            this.Config = config;
            this.TraceWriter = traceWriter;
            this.EventHubConfiguration = new EventHubConfiguration();
            this.ApiHubConfiguration = new ApiHubConfiguration();
        }

        internal JobHostConfiguration Config { get; private set; }

        internal TraceWriter TraceWriter { get; private set; }

        internal EventHubConfiguration EventHubConfiguration { get; private set; }

        internal ApiHubConfiguration ApiHubConfiguration { get; set; }

        public void Done()
        {
            this.Config.UseEventHub(this.EventHubConfiguration);
            this.Config.UseMobileApps();
            this.Config.UseDocumentDB();
            this.Config.UseNotificationHubs();
            this.Config.UseApiHub(this.ApiHubConfiguration);
        }
    }
}