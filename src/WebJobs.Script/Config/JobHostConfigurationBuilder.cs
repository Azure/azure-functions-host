using Microsoft.Azure.WebJobs.ServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Script.Description
{    
    // Helper to collect configuration updates from the BindingMetadata and ultimately apply to the JobHostConfiguration. 
    public class JobHostConfigurationBuilder
    {
        internal JobHostConfiguration Config;

        public JobHostConfigurationBuilder(JobHostConfiguration config)
        {
            this.Config = config;
        }

        internal EventHubConfiguration EventHubConfiguration = new EventHubConfiguration();

        public void Done()
        {
            this.Config.UseEventHub(this.EventHubConfiguration);
        }
    }
}