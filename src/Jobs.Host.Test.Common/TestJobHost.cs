using Microsoft.WindowsAzure.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.WindowsAzure.Jobs.Test
{
    // Helper for calling individual methods. 
    public class TestJobHost<T>
    {
        private const string DeveloperAccountConnectionString = "UseDevelopmentStorage=true";
        public JobHost Host { get; private set; }

        public TestJobHost()
            : this(DeveloperAccountConnectionString)
        {
        }

        // accountConnectionString can be null if the test is really sure that it's not using any storage operations. 
        public TestJobHost(string accountConnectionString)
        {
            var hooks = new JobHostTestHooks
            {
                StorageValidator = new NullStorageValidator(),
                TypeLocator = new SimpleTypeLocator(typeof(T))
            };

            // If there is an indexing error, we'll throw here. 
            // use null logging string since unit tests don't need logs. 
            Host = new JobHost(accountConnectionString, null, hooks);
        }

        public void Call(string methodName)
        {
            var methodInfo = typeof(T).GetMethod(methodName);
            Host.Call(methodInfo);
        }
    }  
}
