// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    // Helper for calling individual methods. 
    public class TestJobHost<T>
    {
        private const string DeveloperStorageConnectionString = "UseDevelopmentStorage=true";
        public JobHost Host { get; private set; }

        public TestJobHost()
            : this(CloudStorageAccount.DevelopmentStorageAccount)
        {
        }

        // storageAccount can be null if the test is really sure that it's not using any storage operations. 
        public TestJobHost(CloudStorageAccount storageAccount)
        {
            TestJobHostConfiguration configuration = new TestJobHostConfiguration
            {
                TypeLocator = new SimpleTypeLocator(typeof(T)),
                StorageAccountProvider = new SimpleStorageAccountProvider
                {
                    StorageAccount = storageAccount,
                    // use null logging string since unit tests don't need logs. 
                    DashboardAccount = null
                },
                StorageCredentialsValidator = new NullStorageCredentialsValidator(),
                ConnectionStringProvider = new NullConnectionStringProvider()
            };

            // If there is an indexing error, we'll throw here. 
            Host = new JobHost(configuration);
        }

        public void Call(string methodName)
        {
            Call(methodName, null);
        }

        public void Call(string methodName, object arguments)
        {
            var methodInfo = typeof(T).GetMethod(methodName);
            Host.Call(methodInfo, arguments);
        }

        public Task CallAsync(string methodName, object arguments, CancellationToken cancellationToken)
        {
            var methodInfo = typeof(T).GetMethod(methodName);
            return Host.CallAsync(methodInfo, arguments, cancellationToken);
        }
    }  
}
