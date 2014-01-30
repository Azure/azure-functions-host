using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    public class JobHostTests
    {
        [Fact]
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
                "Failed to validate Windows Azure Jobs runtime connection string: Windows Azure Storage account connection string is missing or empty." + Environment.NewLine + "The Windows Azure Jobs connection string is specified by setting a connection string named 'AzureJobsRuntime' in the connectionStrings section of the .config file, or with an environment variable named 'AzureJobsRuntime', or by using a constructor for JobHost that accepts connection strings.");
        }
    }
}
