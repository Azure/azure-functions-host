using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    // Helper for calling individual methods. 
    public class TestJobHost<T>
    {
        private const string DeveloperStorageConnectionString = "UseDevelopmentStorage=true";
        public JobHost Host { get; private set; }

        public TestJobHost()
            : this(DeveloperStorageConnectionString)
        {
        }

        // accountConnectionString can be null if the test is really sure that it's not using any storage operations. 
        public TestJobHost(string storageConnectionString)
        {
            TestJobHostConfiguration configuration = new TestJobHostConfiguration
            {
                StorageValidator = new NullStorageValidator(),
                TypeLocator = new SimpleTypeLocator(typeof(T)),
                ConnectionStringProvider = new SimpleConnectionStringProvider
                {
                    StorageConnectionString = storageConnectionString,
                    // use null logging string since unit tests don't need logs. 
                    DashboardConnectionString = null
                }
            };

            // If there is an indexing error, we'll throw here. 
            Host = new JobHost(configuration);
        }

        public void Call(string methodName)
        {
            Call(methodName, null);
        }

        public void Call(string methodName, IDictionary<string, object> arguments)
        {
            var methodInfo = typeof(T).GetMethod(methodName);
            Host.Call(methodInfo, arguments);
        }

        public void Call(string methodName, IDictionary<string, object> arguments, CancellationToken cancellationToken)
        {
            var methodInfo = typeof(T).GetMethod(methodName);
            Host.Call(methodInfo, arguments, cancellationToken);
        }
    }  
}
