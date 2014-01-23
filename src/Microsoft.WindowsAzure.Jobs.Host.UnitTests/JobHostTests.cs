using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Test;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    [TestClass]
    public class JobHostTests
    {
        [TestMethod]
        public void DefaultConstructor_NoRuntimeConnectionString_Throw()
        {
            var hooks = new JobHostTestHooks
            {
                StorageValidator = new NullStorageValidator(),
                TypeLocator = new SimpleTypeLocator(),
                ConnectionStringProvider = new DictionaryConnectionStringProvider(new Dictionary<string, string>
                {
                    { JobHost.DataConnectionStringName, "SOME_DATA_CONNECTION_STRING" },
                    { JobHost.LoggingConnectionStringName, null }
                })
            };

            // Act & Assert
            ExceptionAssert.ThrowsInvalidOperation(() => 
                new JobHost(hooks), 
                "Failed to validate Windows Azure Jobs runtime connection string: Windows Azure Storage account connection string is missing or empty." + Environment.NewLine + "The Windows Azure Jobs connection string is specified by setting a connection string named 'AzureJobsRuntime' in the connectionStrings section of the .config file, or with an environment variable named 'AzureJobsRuntime', or by using the constructor for JobHost that accepts connection strings.");
        }
    }
}
